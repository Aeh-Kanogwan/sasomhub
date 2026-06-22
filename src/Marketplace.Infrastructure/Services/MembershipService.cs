using Marketplace.Application.Auditing;
using Marketplace.Application.Common;
using Marketplace.Application.Memberships;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// Membership lifecycle (FR-05/FR-22/FR-27). 3-month free trial from signup; annual paid tiers priced from
/// config (Normal/Verified/Premium) — never hardcoded here; the charged price is snapshotted onto
/// <see cref="Membership.PaidAmountTHB"/> so later ConfigVersions price changes never apply retroactively (Y-06).
/// All fees are billed via <see cref="FeeInvoice"/> (company revenue) — fully separate from buyer↔seller money (no-touch).
/// </summary>
public sealed class MembershipService : IMembershipService
{
    private const int TrialMonthsDefault = 3;
    private static readonly MembershipStatus[] LiveStatuses = { MembershipStatus.Trial, MembershipStatus.Active };

    private readonly MarketplaceDbContext _db;
    private readonly ConfigVersionResolver _config;
    private readonly IAuditService _audit;

    public MembershipService(MarketplaceDbContext db, ConfigVersionResolver config, IAuditService audit)
    {
        _db = db;
        _config = config;
        _audit = audit;
    }

    /// <summary>FR-27: create the TRIAL membership at signup; trialEnd = signup + 3 months (length from config).</summary>
    public async Task<Result<MembershipDto>> StartTrialAsync(StartTrialRequest request, CancellationToken ct = default)
    {
        var tier = await _db.MembershipTiers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MembershipTierId == request.MembershipTierId && t.IsActive, ct);
        if (tier is null)
            return Result<MembershipDto>.Fail("Membership tier not found or inactive.");

        // One live (Trial/Active) membership per user (UX_Membership_LivePerUser).
        var hasLive = await _db.Memberships.AsNoTracking()
            .AnyAsync(m => m.UserId == request.UserId && LiveStatuses.Contains(m.Status), ct);
        if (hasLive)
            return Result<MembershipDto>.Fail("User already has a live membership.");

        // TODO(kyc): Verified/Premium tiers require completed KYC (MembershipTier.RequiresKyc, FR-02/FR-06).
        //            Enforce once the KYC service is available.

        var now = DateTime.UtcNow;
        var trialMonths = await _config.GetIntAsync(ConfigKeys.MembershipTrialMonths, now, TrialMonthsDefault, ct);

        var membership = new Membership
        {
            UserId = request.UserId,
            MembershipTierId = tier.MembershipTierId,
            Status = MembershipStatus.Trial,
            StartAtUtc = now,
            TrialStartsAtUtc = now,
            TrialEndsAtUtc = now.AddMonths(trialMonths), // CK_Membership_TrialHasEnd: trial must have an end
            PaidThroughUtc = null,
            PaidAmountTHB = null,                         // NULL while in trial
            AutoRenew = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.Memberships.Add(membership);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) // lost race on UX_Membership_LivePerUser
        {
            return Result<MembershipDto>.Fail("User already has a live membership.");
        }

        // TODO(disclosure): surface annual price (ResolveAnnualPriceAsync) + trial end to the caller/UI at signup
        //                   (FR-27 consumer-protection: disclose price & expiry up front).
        return Result<MembershipDto>.Success(ToDto(membership));
    }

    /// <summary>
    /// FR-27: request renewal — issue a FeeInvoice (Issued) ONLY. Does NOT activate/extend the membership;
    /// that happens in <see cref="ConfirmInvoicePaidAsync"/> once the fee is confirmed paid (Flow B). Fixes the
    /// "renew-before-pay" loophole: the user gets a pending invoice, not an immediate paid year.
    /// </summary>
    public async Task<Result<MembershipDto>> RenewAsync(RenewMembershipRequest request, CancellationToken ct = default)
    {
        var membership = await _db.Memberships
            .FirstOrDefaultAsync(m => m.MembershipId == request.MembershipId, ct);
        if (membership is null)
            return Result<MembershipDto>.Fail("Membership not found.");
        if (membership.Status == MembershipStatus.Cancelled)
            return Result<MembershipDto>.Fail("Membership is cancelled.");

        var tier = await _db.MembershipTiers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MembershipTierId == membership.MembershipTierId, ct);
        if (tier is null)
            return Result<MembershipDto>.Fail("Membership tier not found.");

        var now = DateTime.UtcNow;
        var price = await ResolveAnnualPriceAsync(tier, now, ct);

        // TODO(auto-renew precondition): when this renewal is triggered by the auto-renew worker, it MUST first
        //   verify the 14/3/1-day advance notices exist in the Notification log (FR-27/FR-32 / consumer law).
        //   No notice record == no charge. Manual user-initiated renewal does not need this gate.

        // Record the user's auto-renew preference now (does not itself extend the paid term).
        membership.AutoRenew = request.AutoRenew;
        membership.UpdatedAtUtc = now;

        // FR-22: platform revenue, billed separately from buyer↔seller money (no commission %).
        // Issued ONLY — the membership is NOT extended until the fee is confirmed paid (Flow B).
        var invoice = new FeeInvoice
        {
            UserId = membership.UserId,
            FeeType = FeeType.MembershipRenewal,
            RelatedMembershipId = membership.MembershipId,
            Amount = price,
            Currency = "THB",
            Status = FeeInvoiceStatus.Issued,
            IssuedAtUtc = now,
        };
        _db.FeeInvoices.Add(invoice);

        // FR-24: audit the renewal-invoice issuance (the money effect happens later on payment confirmation).
        _audit.Write(
            action: "Membership.RenewInvoiceIssued",
            entityType: "Membership",
            entityId: membership.MembershipId.ToString("N"),
            actorUserId: membership.UserId,
            after: new { membership.MembershipId, FeeType = FeeType.MembershipRenewal.ToString(), Amount = price, membership.AutoRenew });

        await _db.SaveChangesAsync(ct);
        return Result<MembershipDto>.Success(ToDto(membership));
    }

    /// <summary>
    /// FR-05: request a tier upgrade — issue a pro-rated FeeInvoice (Issued) ONLY and record the target tier in
    /// <see cref="Membership.PendingUpgradeTierId"/>. Does NOT switch tier; that happens in
    /// <see cref="ConfirmInvoicePaidAsync"/> once the fee is confirmed paid (Flow B).
    /// </summary>
    public async Task<Result<MembershipDto>> UpgradeTierAsync(UpgradeTierRequest request, CancellationToken ct = default)
    {
        var membership = await _db.Memberships
            .FirstOrDefaultAsync(m => m.MembershipId == request.MembershipId, ct);
        if (membership is null)
            return Result<MembershipDto>.Fail("Membership not found.");
        if (membership.Status != MembershipStatus.Active)
            return Result<MembershipDto>.Fail("Only an active (paid) membership can be upgraded.");
        if (request.NewTierId == membership.MembershipTierId)
            return Result<MembershipDto>.Fail("Already on the requested tier.");

        var newTier = await _db.MembershipTiers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MembershipTierId == request.NewTierId && t.IsActive, ct);
        var currentTier = await _db.MembershipTiers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MembershipTierId == membership.MembershipTierId, ct);
        if (newTier is null || currentTier is null)
            return Result<MembershipDto>.Fail("Membership tier not found or inactive.");

        var now = DateTime.UtcNow;
        var newPrice = await ResolveAnnualPriceAsync(newTier, now, ct);
        var currentPrice = await ResolveAnnualPriceAsync(currentTier, now, ct);
        if (newPrice <= currentPrice)
            return Result<MembershipDto>.Fail("Target tier is not an upgrade (price not higher).");

        // Pro-rate the difference over the fraction of the paid year still remaining.
        var prorated = ProrateDifference(currentPrice, newPrice, membership.PaidThroughUtc, now);

        // Record the requested target — tier is NOT switched until the fee is confirmed paid (Flow B).
        membership.PendingUpgradeTierId = newTier.MembershipTierId;
        membership.UpdatedAtUtc = now;

        _db.FeeInvoices.Add(new FeeInvoice
        {
            UserId = membership.UserId,
            FeeType = FeeType.MembershipUpgrade,
            RelatedMembershipId = membership.MembershipId,
            Amount = prorated,
            Currency = "THB",
            Status = FeeInvoiceStatus.Issued,
            IssuedAtUtc = now,
        });

        // FR-24: audit the upgrade request (tier is NOT switched until the fee is confirmed paid).
        _audit.Write(
            action: "Membership.UpgradeRequested",
            entityType: "Membership",
            entityId: membership.MembershipId.ToString("N"),
            actorUserId: membership.UserId,
            before: new { CurrentTierId = membership.MembershipTierId },
            after: new { PendingUpgradeTierId = newTier.MembershipTierId, ProratedAmount = prorated });

        await _db.SaveChangesAsync(ct);

        // TODO(kyc): if newTier.RequiresKyc, enforce completed KYC before allowing the upgrade.
        return Result<MembershipDto>.Success(ToDto(membership));
    }

    /// <summary>
    /// Apply the membership effect of a now-PAID membership FeeInvoice (Flow B confirmation). Idempotent:
    ///   • Membership/MembershipRenewal → Status=Active, PaidThroughUtc += 1 year (from max(now, PaidThroughUtc)),
    ///     snapshot PaidAmountTHB = invoice Amount.
    ///   • MembershipUpgrade → switch MembershipTierId to PendingUpgradeTierId, then clear it.
    /// Returns failure only on genuinely missing/mismatched data; a no-op repeat call returns Success.
    /// Does NOT modify the invoice (the caller — PaymentSlipService — owns the invoice Paid transition).
    /// </summary>
    public async Task<Result<MembershipDto>> ConfirmInvoicePaidAsync(Guid feeInvoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.FeeInvoices.AsNoTracking()
            .FirstOrDefaultAsync(f => f.FeeInvoiceId == feeInvoiceId, ct);
        if (invoice is null)
            return Result<MembershipDto>.Fail("Fee invoice not found.");
        if (invoice.RelatedMembershipId is not { } membershipId)
            return Result<MembershipDto>.Fail("Fee invoice is not linked to a membership.");
        if (!IsMembershipFee(invoice.FeeType))
            return Result<MembershipDto>.Fail("Fee invoice is not a membership fee.");

        var membership = await _db.Memberships
            .FirstOrDefaultAsync(m => m.MembershipId == membershipId, ct);
        if (membership is null)
            return Result<MembershipDto>.Fail("Membership not found.");

        var now = DateTime.UtcNow;

        if (invoice.FeeType == FeeType.MembershipUpgrade)
        {
            // Apply the recorded target tier (idempotent: if already applied/cleared, just succeed).
            if (membership.PendingUpgradeTierId is { } targetTierId &&
                membership.MembershipTierId != targetTierId)
            {
                var fromTierId = membership.MembershipTierId;
                membership.MembershipTierId = targetTierId;
                membership.PaidAmountTHB = await ResolveTierPriceAsync(targetTierId, now, ct);
                membership.UpdatedAtUtc = now;

                // FR-24: audit the applied tier switch (only when it actually changes — idempotent re-runs do not re-audit).
                _audit.Write(
                    action: "Membership.UpgradeApplied",
                    entityType: "Membership",
                    entityId: membership.MembershipId.ToString("N"),
                    actorUserId: membership.UserId,
                    before: new { TierId = fromTierId },
                    after: new { TierId = targetTierId, PaidAmountTHB = membership.PaidAmountTHB, FeeInvoiceId = feeInvoiceId });
            }
            // Clear the pending marker once consumed.
            if (membership.PendingUpgradeTierId is not null)
            {
                membership.PendingUpgradeTierId = null;
                membership.UpdatedAtUtc = now;
            }
        }
        else // Membership / MembershipRenewal: activate + extend one year.
        {
            var alreadyApplied = membership.Status == MembershipStatus.Active
                && membership.PaidThroughUtc is { } pt
                && pt > invoice.IssuedAtUtc; // a paid term that postdates this invoice was already granted

            if (!alreadyApplied)
            {
                var fromStatus = membership.Status;
                var fromPaidThrough = membership.PaidThroughUtc;
                var baseInstant = membership.PaidThroughUtc is { } cur && cur > now ? cur : now;
                membership.Status = MembershipStatus.Active;
                membership.PaidThroughUtc = baseInstant.AddYears(1);
                membership.PaidAmountTHB = invoice.Amount; // Y-06: snapshot the charged price (no retroactive pricing)
                membership.UpdatedAtUtc = now;

                // FR-24: audit the applied activation/renewal (only on the first effective confirmation).
                _audit.Write(
                    action: "Membership.RenewApplied",
                    entityType: "Membership",
                    entityId: membership.MembershipId.ToString("N"),
                    actorUserId: membership.UserId,
                    before: new { Status = fromStatus.ToString(), PaidThroughUtc = fromPaidThrough },
                    after: new { Status = MembershipStatus.Active.ToString(), membership.PaidThroughUtc, PaidAmountTHB = invoice.Amount, FeeInvoiceId = feeInvoiceId });
            }
        }

        await _db.SaveChangesAsync(ct);
        return Result<MembershipDto>.Success(ToDto(membership));
    }

    private static bool IsMembershipFee(FeeType feeType) =>
        feeType is FeeType.Membership or FeeType.MembershipRenewal or FeeType.MembershipUpgrade;

    private async Task<decimal> ResolveTierPriceAsync(byte tierId, DateTime asOfUtc, CancellationToken ct)
    {
        var tier = await _db.MembershipTiers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MembershipTierId == tierId, ct);
        return tier is null ? 0m : await ResolveAnnualPriceAsync(tier, asOfUtc, ct);
    }

    /// <summary>
    /// FR-06/FR-10 gate: true if the user currently has a live membership — Trial within its window,
    /// or paid Active still within PaidThroughUtc. Registration alone is NOT enough (must be active).
    /// </summary>
    public async Task<bool> IsMembershipActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Memberships.AsNoTracking().AnyAsync(m =>
            m.UserId == userId &&
            ((m.Status == MembershipStatus.Trial && m.TrialEndsAtUtc != null && m.TrialEndsAtUtc > now) ||
             (m.Status == MembershipStatus.Active && m.PaidThroughUtc != null && m.PaidThroughUtc > now)),
            ct);
    }

    /// <summary>
    /// Resolve the annual price from ConfigVersions (source of truth, FR-31/FR-35) using the tier Code,
    /// falling back to the MembershipTiers lookup value. Never hardcodes prices.
    /// </summary>
    private async Task<decimal> ResolveAnnualPriceAsync(MembershipTier tier, DateTime asOfUtc, CancellationToken ct)
    {
        var key = ConfigKeys.MembershipAnnualPrice(tier.Code);
        return await _config.GetDecimalAsync(key, asOfUtc, fallback: tier.AnnualPriceTHB, ct);
    }

    /// <summary>Pro-rate the price difference by the fraction of the current paid year still remaining.</summary>
    private static decimal ProrateDifference(decimal currentPrice, decimal newPrice, DateTime? paidThroughUtc, DateTime now)
    {
        var diff = newPrice - currentPrice;
        if (paidThroughUtc is not { } end || end <= now)
            return diff; // no remaining term info -> charge full difference

        var remainingDays = (end - now).TotalDays;
        var fraction = (decimal)Math.Clamp(remainingDays / 365.0, 0d, 1d);
        return Math.Round(diff * fraction, 2, MidpointRounding.AwayFromZero);
    }

    private static MembershipDto ToDto(Membership m) => new(
        m.MembershipId, m.UserId, m.MembershipTierId, m.Status,
        m.TrialEndsAtUtc, m.PaidThroughUtc, m.AutoRenew);
}
