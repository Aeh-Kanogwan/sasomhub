using Marketplace.Application.Common;

namespace Marketplace.Application.Privacy;

// M3 (PDPA DSAR): export / erasure / access / rectify / withdraw-consent requests (FR-01/04).
// Owns dbo.DataSubjectRequests. Export produces an artifact (path stored, not data inline); erasure
// reuses the soft-delete/anonymize on dbo.Users. Every transition is written to dbo.AuditLogs.

/// <summary>Kind of request (mirrors Domain.Enums.DataSubjectRequestType).</summary>
public enum DsarType { Export, Erasure, Access, Rectify, WithdrawConsent }

/// <summary>A data subject lodges a request about their own data.</summary>
public record CreateDsarRequest(Guid RequestedByUserId, DsarType RequestType, string? Note = null);

public record DataSubjectRequestDto(
    Guid DataSubjectRequestId,
    string RequestType,
    string Status,
    Guid RequestedByUserId,
    Guid? HandledByUserId,
    string? ResultArtifactPath,
    DateTime DueByUtc,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? Note);

/// <summary>M3 PDPA data-subject request service. Append-friendly; transitions are audited.</summary>
public interface IDataSubjectRequestService
{
    /// <summary>FR-01/04: lodge a new request (status Pending, DueByUtc = now + statutory window).</summary>
    Task<Result<DataSubjectRequestDto>> CreateAsync(CreateDsarRequest request, CancellationToken ct = default);

    /// <summary>The data subject reads their own requests.</summary>
    Task<Result<IReadOnlyList<DataSubjectRequestDto>>> GetMyRequestsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Admin queue — list requests filtered by status (NULL = all), oldest-due first.</summary>
    Task<Result<IReadOnlyList<DataSubjectRequestDto>>> GetForAdminAsync(string? status, CancellationToken ct = default);

    /// <summary>Admin: claim/advance a request to InProgress and record the handler.</summary>
    Task<Result<DataSubjectRequestDto>> AssignAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default);

    /// <summary>
    /// M3 Export/Access: generate the data package, store the artifact path, mark Completed.
    /// The actual serialization is the implementer's concern (PDPA scope = the subject's own data).
    /// </summary>
    Task<Result<DataSubjectRequestDto>> FulfilExportAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default);

    /// <summary>
    /// M3 Erasure: anonymize/soft-delete the subject's data (reuses User.IsAnonymized/IsDeleted),
    /// preserving append-only audit/consent/credit rows for legal retention, then mark Completed.
    /// </summary>
    Task<Result<DataSubjectRequestDto>> FulfilErasureAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default);

    /// <summary>Admin: reject a request with a reason (recorded in Note + AuditLogs).</summary>
    Task<Result<DataSubjectRequestDto>> RejectAsync(Guid requestId, Guid handledByUserId, string reason, CancellationToken ct = default);
}
