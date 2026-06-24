using Marketplace.Application.Common;

namespace Marketplace.Application.Catalog;

// M6: create appraiser opinions (currently read-only). FR-07 / IS-8 / LEGAL #2: the platform does NOT
// warrant authenticity — every row is an OPINION and stores the DisclaimerVersion shown at create time.
// Owns the WRITE side of dbo.AppraisalOpinions (draft -> publish). UI/text must avoid "รับประกัน/ของแท้ 100%".

/// <summary>Create an appraisal opinion on a product (FR-07). DisclaimerVersion is captured by the service.</summary>
public record CreateAppraisalRequest(
    Guid ProductId,
    Guid? AppraiserUserId,
    string AppraiserName,
    string OpinionText,
    bool Publish);

public record AppraisalOpinionDto(
    Guid AppraisalOpinionId,
    Guid ProductId,
    Guid? AppraiserUserId,
    string AppraiserName,
    string OpinionText,
    string DisclaimerVersion,
    bool IsPublished,
    DateTime CreatedAtUtc);

/// <summary>M6 appraisal write service (FR-07, LEGAL #2 — opinion only, never a warranty).</summary>
public interface IAppraisalService
{
    /// <summary>
    /// FR-07: create an opinion, stamping the CURRENT disclaimer version (proof of which text applied).
    /// Optionally publishes immediately; otherwise saved unpublished for review.
    /// </summary>
    Task<Result<AppraisalOpinionDto>> CreateAsync(CreateAppraisalRequest request, CancellationToken ct = default);

    /// <summary>Publish a previously-drafted opinion (separate review step before it shows publicly).</summary>
    Task<Result<AppraisalOpinionDto>> PublishAsync(Guid appraisalOpinionId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Unpublish a previously-published opinion (pull it back into review / hide from the public list).</summary>
    Task<Result<AppraisalOpinionDto>> UnpublishAsync(Guid appraisalOpinionId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Opinions on a product. <paramref name="includeUnpublished"/>=false (default) returns only the
    /// publicly-visible (published) opinions; the admin/appraiser surface passes true to see drafts too.
    /// </summary>
    Task<Result<IReadOnlyList<AppraisalOpinionDto>>> GetForProductAsync(
        Guid productId, bool includeUnpublished = false, CancellationToken ct = default);
}
