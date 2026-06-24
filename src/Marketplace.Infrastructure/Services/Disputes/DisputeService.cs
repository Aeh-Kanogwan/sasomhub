using Marketplace.Application.Auditing;
using Marketplace.Application.Common;
using Marketplace.Application.Disputes;
using Marketplace.Application.Reputation;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Disputes;

/// <summary>
/// M4 — FR-21 dispute MEDIATION over an existing no-touch Transaction.
///
/// LEGAL INVARIANT (must not regress): the platform is a MEDIATOR only. NOTHING here receives,
/// holds, escrows, pays or refunds buyer/seller money. A dispute records a complaint and the
/// mediation OUTCOME (Open -> UnderReview -> Resolved | Rejected | Escalated). Consequences are
/// limited to recording a resolution and, optionally, a TrustScore adjustment via
/// <see cref="ITrustScoreService"/> using a standard reason code (no defamatory free text, Legal #3).
///
/// Every transition is written to dbo.AuditLogs (FR-24) in the SAME SaveChanges as the action.
/// </summary>
public sealed class DisputeService : IDisputeService
{
    private readonly MarketplaceDbContext _db;
    private readonly IAuditService _audit;
    private readonly ITrustScoreService _trustScore;
    private readonly ILogger<DisputeService> _logger;

    public DisputeService(
        MarketplaceDbContext db,
        IAuditService audit,
        ITrustScoreService trustScore,
        ILogger<DisputeService> logger)
    {
        _db = db;
        _audit = audit;
        _trustScore = trustScore;
        _logger = logger;
    }

    /// <summary>
    /// FR-21: a party to the transaction raises a dispute (status Open), citing a standard reason
    /// code. Rejects non-parties and a second open/under-review dispute on the same transaction.
    /// No money moves — this only records the complaint.
    /// </summary>
    public async Task<Result<DisputeDto>> RaiseAsync(RaiseDisputeRequest request, CancellationToken ct = default)
    {
        var tx = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == request.TransactionId, ct);
        if (tx is null)
            return Result<DisputeDto>.Fail("Transaction not found.");

        // FR-21: only a party (buyer or seller) of the transaction may raise a dispute on it.
        if (request.RaisedByUserId != tx.BuyerId && request.RaisedByUserId != tx.SellerId)
            return Result<DisputeDto>.Fail("Only a party to the transaction (buyer or seller) can raise a dispute.");

        // Standard reason code only (Legal #3 — no free-text accusations as the primary classifier).
        var reasonExists = await _db.BlacklistReasonCodes
            .AnyAsync(r => r.ReasonCodeId == request.ReasonCodeId && r.IsActive, ct);
        if (!reasonExists)
            return Result<DisputeDto>.Fail("Invalid or inactive reason code.");

        // Guard against duplicate concurrent disputes: one active (Open/UnderReview) dispute per transaction.
        var alreadyActive = await _db.Disputes.AsNoTracking().AnyAsync(
            d => d.TransactionId == request.TransactionId
                 && (d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview),
            ct);
        if (alreadyActive)
            return Result<DisputeDto>.Fail("An active dispute already exists for this transaction.");

        var now = DateTime.UtcNow;
        var dispute = new Dispute
        {
            DisputeId = Guid.NewGuid(),
            TransactionId = request.TransactionId,
            RaisedByUserId = request.RaisedByUserId,
            ReasonCodeId = request.ReasonCodeId,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Status = DisputeStatus.Open,
            CreatedAtUtc = now,
        };
        _db.Disputes.Add(dispute);

        // FR-24: audit the open. No money movement — this is a mediation record only.
        _audit.Write(
            action: "Dispute.Raised",
            entityType: "Dispute",
            entityId: dispute.DisputeId.ToString("N"),
            actorUserId: request.RaisedByUserId,
            after: new
            {
                dispute.DisputeId,
                dispute.TransactionId,
                dispute.RaisedByUserId,
                dispute.ReasonCodeId,
                Status = dispute.Status.ToString(),
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Dispute {DisputeId} raised on transaction {TxId} by {UserId}.",
            dispute.DisputeId, dispute.TransactionId, request.RaisedByUserId);

        return Result<DisputeDto>.Success(ToDto(dispute));
    }

    public async Task<Result<DisputeDto>> GetAsync(Guid disputeId, CancellationToken ct = default)
    {
        var dispute = await _db.Disputes.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DisputeId == disputeId, ct);
        return dispute is null
            ? Result<DisputeDto>.Fail("Dispute not found.")
            : Result<DisputeDto>.Success(ToDto(dispute));
    }

    /// <summary>A user's disputes: raised by them OR on a transaction where they are a party.</summary>
    public async Task<Result<IReadOnlyList<DisputeDto>>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var disputes = await _db.Disputes.AsNoTracking()
            .Where(d => d.RaisedByUserId == userId
                        || d.Transaction.BuyerId == userId
                        || d.Transaction.SellerId == userId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(ct);

        IReadOnlyList<DisputeDto> dtos = disputes.Select(ToDto).ToList();
        return Result<IReadOnlyList<DisputeDto>>.Success(dtos);
    }

    /// <summary>Admin queue — disputes by status (NULL/empty = all), oldest-first (FIFO triage).</summary>
    public async Task<Result<IReadOnlyList<DisputeDto>>> GetForAdminAsync(string? status, CancellationToken ct = default)
    {
        var query = _db.Disputes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DisputeStatus>(status, ignoreCase: true, out var parsed))
                return Result<IReadOnlyList<DisputeDto>>.Fail($"Unknown dispute status '{status}'.");
            query = query.Where(d => d.Status == parsed);
        }

        var disputes = await query
            .OrderBy(d => d.CreatedAtUtc)
            .ToListAsync(ct);

        IReadOnlyList<DisputeDto> dtos = disputes.Select(ToDto).ToList();
        return Result<IReadOnlyList<DisputeDto>>.Success(dtos);
    }

    /// <summary>
    /// Admin/Support transition: Open -> UnderReview -> Resolved | Rejected | Escalated.
    /// Records the resolution + handler, stamps ResolvedAtUtc on terminal states, and (best-effort)
    /// feeds TrustScore on a Resolved outcome. No money is paid or refunded.
    /// </summary>
    public async Task<Result<DisputeDto>> TransitionAsync(ResolveDisputeRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<DisputeStatus>(request.NewStatus, ignoreCase: true, out var target))
            return Result<DisputeDto>.Fail($"Unknown dispute status '{request.NewStatus}'.");

        var dispute = await _db.Disputes
            .FirstOrDefaultAsync(d => d.DisputeId == request.DisputeId, ct);
        if (dispute is null)
            return Result<DisputeDto>.Fail("Dispute not found.");

        var from = dispute.Status;
        if (!IsAllowedTransition(from, target))
            return Result<DisputeDto>.Fail($"Cannot transition dispute from {from} to {target}.");

        // Terminal outcomes (Resolved/Rejected) require a recorded resolution rationale.
        var isTerminal = target is DisputeStatus.Resolved or DisputeStatus.Rejected;
        if (isTerminal && string.IsNullOrWhiteSpace(request.Resolution))
            return Result<DisputeDto>.Fail("A resolution note is required to resolve or reject a dispute.");

        var now = DateTime.UtcNow;
        dispute.Status = target;
        dispute.HandledByUserId = request.HandledByUserId;
        if (!string.IsNullOrWhiteSpace(request.Resolution))
            dispute.Resolution = request.Resolution.Trim();
        if (isTerminal)
            dispute.ResolvedAtUtc = now;

        _audit.Write(
            action: "Dispute.Transition",
            entityType: "Dispute",
            entityId: dispute.DisputeId.ToString("N"),
            actorUserId: request.HandledByUserId,
            before: new { Status = from.ToString() },
            after: new
            {
                Status = target.ToString(),
                dispute.HandledByUserId,
                dispute.ResolvedAtUtc,
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Dispute {DisputeId} transitioned {From} -> {To} by {Admin}.",
            dispute.DisputeId, from, target, request.HandledByUserId);

        // FR-14 (optional, fail-soft): a Resolved dispute may dock the responsible party's trust score
        // using the dispute's own standard reason code. NO money moves — this is reputation only, and a
        // failure here must never block the mediation outcome that already committed above.
        if (target == DisputeStatus.Resolved)
            await TryApplyTrustConsequenceAsync(dispute, request.HandledByUserId, ct);

        return Result<DisputeDto>.Success(ToDto(dispute));
    }

    /// <summary>
    /// Lifecycle guard (CK_Dispute_Status flow). Open -> UnderReview | Resolved | Rejected | Escalated;
    /// UnderReview -> Resolved | Rejected | Escalated; Escalated -> Resolved | Rejected.
    /// Terminal states (Resolved/Rejected) are final.
    /// </summary>
    private static bool IsAllowedTransition(DisputeStatus from, DisputeStatus to) => from switch
    {
        DisputeStatus.Open => to is DisputeStatus.UnderReview or DisputeStatus.Resolved
                                    or DisputeStatus.Rejected or DisputeStatus.Escalated,
        DisputeStatus.UnderReview => to is DisputeStatus.Resolved or DisputeStatus.Rejected
                                           or DisputeStatus.Escalated,
        DisputeStatus.Escalated => to is DisputeStatus.Resolved or DisputeStatus.Rejected,
        _ => false, // Resolved / Rejected are terminal
    };

    /// <summary>
    /// Best-effort TrustScore consequence on a Resolved dispute (FR-14). Docks the COUNTERPARTY of the
    /// raiser on the transaction, using the dispute's standard reason code's default penalty. Reputation
    /// only — never money. Fails soft so the (already-committed) resolution is never blocked.
    /// </summary>
    private async Task TryApplyTrustConsequenceAsync(Dispute dispute, Guid? actorUserId, CancellationToken ct)
    {
        try
        {
            if (dispute.ReasonCodeId is not int reasonCodeId)
                return;

            var tx = await _db.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TransactionId == dispute.TransactionId, ct);
            if (tx is null)
                return;

            // The party complained about is the other side of the deal from the raiser.
            var respondentId = dispute.RaisedByUserId == tx.BuyerId ? tx.SellerId : tx.BuyerId;

            var result = await _trustScore.ApplyEventAsync(new ApplyTrustEventRequest(
                UserId: respondentId,
                ReasonCodeId: reasonCodeId,
                RelatedTransactionId: dispute.TransactionId,
                ActorUserId: actorUserId,
                Note: $"Dispute {dispute.DisputeId:N} resolved (FR-21)."), ct);

            if (!result.Succeeded)
                _logger.LogWarning("Trust consequence failed for dispute {DisputeId}: {Error}",
                    dispute.DisputeId, result.Error);
        }
        catch (Exception ex)
        {
            // Never let a reputation side effect roll back / block the resolved mediation.
            _logger.LogWarning(ex, "Trust consequence threw for dispute {DisputeId}; resolution stands.",
                dispute.DisputeId);
        }
    }

    private static DisputeDto ToDto(Dispute d) => new(
        d.DisputeId,
        d.TransactionId,
        d.RaisedByUserId,
        d.ReasonCodeId,
        d.Description,
        d.Status.ToString(),
        d.Resolution,
        d.HandledByUserId,
        d.CreatedAtUtc,
        d.ResolvedAtUtc);
}
