using Marketplace.Application.Common;
using Marketplace.Application.Memberships;
using Marketplace.Application.Payments;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// Flow B membership payment (FR-22/FR-35): a member uploads a bank-transfer slip against an Issued company
/// <see cref="FeeInvoice"/> (membership fee); an Admin Approves it (→ invoice Paid + membership renew/upgrade)
/// or Rejects it. Membership fees are COMPANY money, fully separate from buyer↔seller trade money — receiving
/// and confirming them does NOT touch the no-touch trade flow (there is still no wallet/escrow for trade money).
/// All Approve/Reject decisions are written to the append-only AuditLogs (FR-24).
/// </summary>
public sealed class PaymentSlipService : IPaymentSlipService
{
    private readonly MarketplaceDbContext _db;
    private readonly IMembershipService _membershipService;
    private readonly ILogger<PaymentSlipService> _logger;

    public PaymentSlipService(
        MarketplaceDbContext db,
        IMembershipService membershipService,
        ILogger<PaymentSlipService> logger)
    {
        _db = db;
        _membershipService = membershipService;
        _logger = logger;
    }

    public async Task<Result<PaymentSlipDto>> SubmitSlipAsync(SubmitSlipRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SlipImageUrl))
            return Result<PaymentSlipDto>.Fail("Slip image is required.");
        if (request.AmountClaimed < 0m)
            return Result<PaymentSlipDto>.Fail("Amount claimed cannot be negative.");

        var invoice = await _db.FeeInvoices.AsNoTracking()
            .FirstOrDefaultAsync(f => f.FeeInvoiceId == request.FeeInvoiceId, ct);
        if (invoice is null)
            return Result<PaymentSlipDto>.Fail("Fee invoice not found.");
        if (invoice.UserId != request.UserId)
            return Result<PaymentSlipDto>.Fail("This invoice does not belong to you.");
        if (invoice.Status != FeeInvoiceStatus.Issued)
            return Result<PaymentSlipDto>.Fail("This invoice is not awaiting payment.");

        // Anti-duplicate: if a Pending slip for this invoice already exists, return it instead of creating another.
        var existingPending = await _db.PaymentSlips.AsNoTracking()
            .FirstOrDefaultAsync(s => s.FeeInvoiceId == request.FeeInvoiceId && s.Status == PaymentSlipStatus.Pending, ct);
        if (existingPending is not null)
            return Result<PaymentSlipDto>.Success(ToDto(existingPending));

        var now = DateTime.UtcNow;
        var slip = new PaymentSlip
        {
            FeeInvoiceId = request.FeeInvoiceId,
            UserId = request.UserId,
            SlipImageUrl = request.SlipImageUrl.Trim(),
            AmountClaimed = request.AmountClaimed,
            TransferredAtUtc = request.TransferredAtUtc,
            BankRefNote = string.IsNullOrWhiteSpace(request.BankRefNote) ? null : request.BankRefNote.Trim(),
            Status = PaymentSlipStatus.Pending,
            SubmittedAtUtc = now,
        };
        _db.PaymentSlips.Add(slip);
        await _db.SaveChangesAsync(ct);
        return Result<PaymentSlipDto>.Success(ToDto(slip));
    }

    public async Task<Result<PaymentSlipDto>> ApproveSlipAsync(Guid slipId, Guid adminUserId, string? note, CancellationToken ct = default)
    {
        var useTx = _db.Database.IsRelational();
        await using var tx = useTx
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;

        var slip = await _db.PaymentSlips
            .FirstOrDefaultAsync(s => s.PaymentSlipId == slipId, ct);
        if (slip is null)
            return Result<PaymentSlipDto>.Fail("Payment slip not found.");

        // Idempotent: an already-approved slip is a no-op success (e.g. double-click / retry after commit).
        if (slip.Status == PaymentSlipStatus.Approved)
            return Result<PaymentSlipDto>.Success(ToDto(slip));
        if (slip.Status != PaymentSlipStatus.Pending)
            return Result<PaymentSlipDto>.Fail("Only a pending slip can be approved.");

        var invoice = await _db.FeeInvoices
            .FirstOrDefaultAsync(f => f.FeeInvoiceId == slip.FeeInvoiceId, ct);
        if (invoice is null)
            return Result<PaymentSlipDto>.Fail("Linked fee invoice not found.");

        var now = DateTime.UtcNow;

        slip.Status = PaymentSlipStatus.Approved;
        slip.ReviewedByUserId = adminUserId;
        slip.ReviewedAtUtc = now;
        slip.ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // Settle the company invoice (idempotent: only the first approval flips Issued → Paid).
        if (invoice.Status != FeeInvoiceStatus.Paid)
        {
            invoice.Status = FeeInvoiceStatus.Paid;
            invoice.PaidAtUtc = now;
            invoice.ExternalPaymentRef = $"SLIP:{slipId:N}";
        }

        await _db.SaveChangesAsync(ct);

        // Drive the membership effect for membership fees (renew/upgrade). Idempotent inside the service.
        if (IsMembershipFee(invoice.FeeType))
        {
            var confirm = await _membershipService.ConfirmInvoicePaidAsync(invoice.FeeInvoiceId, ct);
            if (!confirm.Succeeded)
            {
                // Do not leave a half-applied state: roll the whole approval back so the admin can retry.
                if (tx is not null) await tx.RollbackAsync(ct);
                _logger.LogWarning("ApproveSlip {SlipId}: membership confirmation failed: {Error}", slipId, confirm.Error);
                return Result<PaymentSlipDto>.Fail(confirm.Error ?? "Failed to apply the membership change.");
            }
        }

        // FR-24: append-only audit of the admin decision.
        WriteAudit(adminUserId, "PaymentSlip.Approve", slip, invoice, slip.ReviewNote);
        await _db.SaveChangesAsync(ct);

        if (tx is not null) await tx.CommitAsync(ct);
        return Result<PaymentSlipDto>.Success(ToDto(slip));
    }

    public async Task<Result<PaymentSlipDto>> RejectSlipAsync(Guid slipId, Guid adminUserId, string? note, CancellationToken ct = default)
    {
        var slip = await _db.PaymentSlips
            .FirstOrDefaultAsync(s => s.PaymentSlipId == slipId, ct);
        if (slip is null)
            return Result<PaymentSlipDto>.Fail("Payment slip not found.");

        // Idempotent: an already-rejected slip is a no-op success.
        if (slip.Status == PaymentSlipStatus.Rejected)
            return Result<PaymentSlipDto>.Success(ToDto(slip));
        if (slip.Status != PaymentSlipStatus.Pending)
            return Result<PaymentSlipDto>.Fail("Only a pending slip can be rejected.");

        var invoice = await _db.FeeInvoices.AsNoTracking()
            .FirstOrDefaultAsync(f => f.FeeInvoiceId == slip.FeeInvoiceId, ct);

        var now = DateTime.UtcNow;
        slip.Status = PaymentSlipStatus.Rejected;
        slip.ReviewedByUserId = adminUserId;
        slip.ReviewedAtUtc = now;
        slip.ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        // The FeeInvoice stays Issued — the member can submit a corrected slip.

        // FR-24: append-only audit of the admin decision.
        WriteAudit(adminUserId, "PaymentSlip.Reject", slip, invoice, slip.ReviewNote);
        await _db.SaveChangesAsync(ct);
        return Result<PaymentSlipDto>.Success(ToDto(slip));
    }

    public async Task<IReadOnlyList<PaymentSlipReviewDto>> ListPendingSlipsAsync(CancellationToken ct = default)
    {
        return await _db.PaymentSlips.AsNoTracking()
            .Where(s => s.Status == PaymentSlipStatus.Pending)
            .OrderBy(s => s.SubmittedAtUtc)
            .Select(s => ToReviewDto(s, s.FeeInvoice, s.User))
            .ToListAsync(ct);
    }

    public async Task<PaymentSlipReviewDto?> GetSlipAsync(Guid slipId, CancellationToken ct = default)
    {
        return await _db.PaymentSlips.AsNoTracking()
            .Where(s => s.PaymentSlipId == slipId)
            .Select(s => ToReviewDto(s, s.FeeInvoice, s.User))
            .FirstOrDefaultAsync(ct);
    }

    private void WriteAudit(Guid adminUserId, string action, PaymentSlip slip, FeeInvoice? invoice, string? note)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = adminUserId,
            Action = action,
            EntityType = "PaymentSlip",
            EntityId = slip.PaymentSlipId.ToString("N"),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                slip.PaymentSlipId,
                slip.FeeInvoiceId,
                SubmitterUserId = slip.UserId,
                slip.AmountClaimed,
                SlipStatus = slip.Status.ToString(),
                InvoiceStatus = invoice?.Status.ToString(),
                InvoiceAmount = invoice?.Amount,
                FeeType = invoice?.FeeType.ToString(),
                Note = note,
            }),
            CreatedAtUtc = DateTime.UtcNow,
        });
    }

    private static bool IsMembershipFee(FeeType feeType) =>
        feeType is FeeType.Membership or FeeType.MembershipRenewal or FeeType.MembershipUpgrade;

    private static PaymentSlipDto ToDto(PaymentSlip s) => new(
        s.PaymentSlipId, s.FeeInvoiceId, s.UserId, s.SlipImageUrl, s.AmountClaimed,
        s.TransferredAtUtc, s.BankRefNote, s.Status, s.SubmittedAtUtc, s.ReviewedAtUtc, s.ReviewNote);

    private static PaymentSlipReviewDto ToReviewDto(PaymentSlip s, FeeInvoice invoice, User user) => new(
        s.PaymentSlipId, s.FeeInvoiceId, s.UserId, user.Email, s.SlipImageUrl, s.AmountClaimed,
        invoice.Amount, invoice.Currency, invoice.FeeType, invoice.Status, s.TransferredAtUtc,
        s.BankRefNote, s.Status, s.SubmittedAtUtc, s.ReviewedAtUtc, s.ReviewNote);
}
