using Marketplace.Application.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Payments;

// Flow B (FR-22/FR-35): membership fee = bank transfer into the company account, member uploads the
// transfer slip, an Admin confirms it in the dashboard. Company money — fully separate from buyer↔seller
// trade money (no-touch preserved). Approval marks the FeeInvoice Paid and drives the membership effect.

public record SubmitSlipRequest(
    Guid FeeInvoiceId,
    Guid UserId,
    string SlipImageUrl,
    decimal AmountClaimed,
    DateTime TransferredAtUtc,
    string? BankRefNote);

/// <summary>One pending/processed payment slip (member-facing summary).</summary>
public record PaymentSlipDto(
    Guid PaymentSlipId,
    Guid FeeInvoiceId,
    Guid UserId,
    string SlipImageUrl,
    decimal AmountClaimed,
    DateTime TransferredAtUtc,
    string? BankRefNote,
    PaymentSlipStatus Status,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewNote);

/// <summary>Admin review row: slip joined with the invoice + submitting user.</summary>
public record PaymentSlipReviewDto(
    Guid PaymentSlipId,
    Guid FeeInvoiceId,
    Guid UserId,
    string UserEmail,
    string SlipImageUrl,
    decimal AmountClaimed,
    decimal InvoiceAmount,
    string Currency,
    FeeType FeeType,
    FeeInvoiceStatus InvoiceStatus,
    DateTime TransferredAtUtc,
    string? BankRefNote,
    PaymentSlipStatus Status,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewNote);

/// <summary>
/// Flow B membership payment: member uploads a bank-transfer slip against an Issued company FeeInvoice;
/// an Admin Approves (→ invoice Paid + membership effect) or Rejects it.
/// </summary>
public interface IPaymentSlipService
{
    /// <summary>
    /// Member submits a transfer slip for an Issued invoice they own. Rejects if the invoice is not theirs
    /// or not Issued. If a Pending slip for the same invoice already exists, returns that one (no duplicate).
    /// </summary>
    Task<Result<PaymentSlipDto>> SubmitSlipAsync(SubmitSlipRequest request, CancellationToken ct = default);

    /// <summary>
    /// Admin approves a Pending slip — in one transaction: slip→Approved, FeeInvoice→Paid (PaidAtUtc=now,
    /// ExternalPaymentRef=SLIP:{id}), and the membership effect for membership fees (renew/upgrade). Idempotent.
    /// Writes an AuditLog (FR-24).
    /// </summary>
    Task<Result<PaymentSlipDto>> ApproveSlipAsync(Guid slipId, Guid adminUserId, string? note, CancellationToken ct = default);

    /// <summary>
    /// Admin rejects a Pending slip — slip→Rejected; the FeeInvoice stays Issued so the member can re-submit.
    /// Writes an AuditLog (FR-24).
    /// </summary>
    Task<Result<PaymentSlipDto>> RejectSlipAsync(Guid slipId, Guid adminUserId, string? note, CancellationToken ct = default);

    /// <summary>Admin queue: all Pending slips, oldest submitted first.</summary>
    Task<IReadOnlyList<PaymentSlipReviewDto>> ListPendingSlipsAsync(CancellationToken ct = default);

    /// <summary>Admin detail: one slip joined with its invoice + submitting user (any status).</summary>
    Task<PaymentSlipReviewDto?> GetSlipAsync(Guid slipId, CancellationToken ct = default);
}
