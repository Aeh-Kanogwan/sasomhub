using Marketplace.Application.Auditing;
using Marketplace.Application.Catalog;
using Marketplace.Application.Common;
using Marketplace.Domain.Entities;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Catalog;

/// <summary>
/// M6 — FR-07 / IS-8 write side of dbo.AppraisalOpinions.
///
/// LEGAL #2 INVARIANT (must not regress): the platform does NOT warrant authenticity. Every row is an
/// OPINION only. On create we STAMP the disclaimer version that applies right now (resolved from the
/// immutable ConfigVersions history via <see cref="ConfigVersionResolver"/>, key
/// <see cref="DisclaimerConfigKey"/>) so we can later prove which disclaimer text was shown. We also
/// REJECT forbidden warranty wording ("รับประกัน", "การันตี", "ของแท้ 100%", "guarantee",
/// "100% authentic", case-insensitive) in OpinionText — an opinion may never read as a guarantee.
///
/// Every create / publish / unpublish is written to dbo.AuditLogs (FR-24) in the SAME SaveChanges as the
/// action so the audit row commits (or rolls back) together with it.
/// </summary>
public sealed class AppraisalService : IAppraisalService
{
    /// <summary>ConfigVersions key holding the current disclaimer version label (e.g. "v1.2").</summary>
    public const string DisclaimerConfigKey = "Disclaimer.AppraisalVersion";

    /// <summary>Fallback stamped when no disclaimer version is configured (kept inside the varchar(20) column).</summary>
    public const string DefaultDisclaimerVersion = "v0-default";

    /// <summary>
    /// Forbidden warranty phrases (LEGAL #2). Case-insensitive substring match. Kept normalised
    /// lower-case; the latin entries also catch mixed case via ToLowerInvariant on the input.
    /// </summary>
    private static readonly string[] ForbiddenPhrases =
    {
        "รับประกัน",
        "การันตี",
        "ของแท้ 100%",
        "guarantee",
        "100% authentic",
    };

    private readonly MarketplaceDbContext _db;
    private readonly ConfigVersionResolver _config;
    private readonly IAuditService _audit;
    private readonly ILogger<AppraisalService> _logger;

    public AppraisalService(
        MarketplaceDbContext db,
        ConfigVersionResolver config,
        IAuditService audit,
        ILogger<AppraisalService> logger)
    {
        _db = db;
        _config = config;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// FR-07: create an opinion bound to a product + appraiser, stamping the CURRENT disclaimer version.
    /// Rejects unknown products, blank appraiser/opinion, and any forbidden warranty wording. Saved as a
    /// draft (IsPublished=false) unless <see cref="CreateAppraisalRequest.Publish"/> is true.
    /// </summary>
    public async Task<Result<AppraisalOpinionDto>> CreateAsync(CreateAppraisalRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.AppraiserName))
            return Result<AppraisalOpinionDto>.Fail("Appraiser name is required.");

        if (string.IsNullOrWhiteSpace(request.OpinionText))
            return Result<AppraisalOpinionDto>.Fail("Opinion text is required.");

        // LEGAL #2: an opinion must never read as a warranty. Reject forbidden wording up front.
        if (TryFindForbiddenPhrase(request.OpinionText, out var phrase))
            return Result<AppraisalOpinionDto>.Fail(
                $"ความเห็นต้องไม่มีถ้อยคำรับประกัน เช่น \"{phrase}\" (LEGAL #2: แพลตฟอร์มไม่รับประกันความแท้ — เป็นความเห็นเท่านั้น).");

        var productExists = await _db.Products.AsNoTracking()
            .AnyAsync(p => p.ProductId == request.ProductId && !p.IsDeleted, ct);
        if (!productExists)
            return Result<AppraisalOpinionDto>.Fail("Product not found.");

        // If an appraiser user id is supplied it must reference a real user (FK_Appraisal_User).
        if (request.AppraiserUserId is Guid appraiserId)
        {
            var userExists = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == appraiserId, ct);
            if (!userExists)
                return Result<AppraisalOpinionDto>.Fail("Appraiser user not found.");
        }

        var now = DateTime.UtcNow;
        var disclaimerVersion = await ResolveDisclaimerVersionAsync(now, ct);

        var opinion = new AppraisalOpinion
        {
            AppraisalOpinionId = Guid.NewGuid(),
            ProductId = request.ProductId,
            AppraiserUserId = request.AppraiserUserId,
            AppraiserName = request.AppraiserName.Trim(),
            OpinionText = request.OpinionText.Trim(),
            DisclaimerVersion = disclaimerVersion,
            IsPublished = request.Publish,
            CreatedAtUtc = now,
        };
        _db.AppraisalOpinions.Add(opinion);

        _audit.Write(
            action: "AppraisalOpinion.Created",
            entityType: "AppraisalOpinion",
            entityId: opinion.AppraisalOpinionId.ToString("N"),
            actorUserId: request.AppraiserUserId,
            after: new
            {
                opinion.AppraisalOpinionId,
                opinion.ProductId,
                opinion.AppraiserUserId,
                opinion.AppraiserName,
                opinion.DisclaimerVersion,
                opinion.IsPublished,
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "AppraisalOpinion {OpinionId} created on product {ProductId} (published={Published}, disclaimer={Disclaimer}).",
            opinion.AppraisalOpinionId, opinion.ProductId, opinion.IsPublished, opinion.DisclaimerVersion);

        return Result<AppraisalOpinionDto>.Success(ToDto(opinion));
    }

    /// <summary>Publish a draft so it shows on the product detail page. Idempotent-safe; audits the change.</summary>
    public async Task<Result<AppraisalOpinionDto>> PublishAsync(Guid appraisalOpinionId, Guid actorUserId, CancellationToken ct = default)
        => await SetPublishedAsync(appraisalOpinionId, actorUserId, publish: true, ct);

    /// <summary>Pull a published opinion back out of the public list (review/hide). Audits the change.</summary>
    public async Task<Result<AppraisalOpinionDto>> UnpublishAsync(Guid appraisalOpinionId, Guid actorUserId, CancellationToken ct = default)
        => await SetPublishedAsync(appraisalOpinionId, actorUserId, publish: false, ct);

    private async Task<Result<AppraisalOpinionDto>> SetPublishedAsync(
        Guid appraisalOpinionId, Guid actorUserId, bool publish, CancellationToken ct)
    {
        var opinion = await _db.AppraisalOpinions
            .FirstOrDefaultAsync(a => a.AppraisalOpinionId == appraisalOpinionId, ct);
        if (opinion is null)
            return Result<AppraisalOpinionDto>.Fail("Appraisal opinion not found.");

        if (opinion.IsPublished == publish)
            return Result<AppraisalOpinionDto>.Fail(
                publish ? "Opinion is already published." : "Opinion is already unpublished.");

        var was = opinion.IsPublished;
        opinion.IsPublished = publish;

        _audit.Write(
            action: publish ? "AppraisalOpinion.Published" : "AppraisalOpinion.Unpublished",
            entityType: "AppraisalOpinion",
            entityId: opinion.AppraisalOpinionId.ToString("N"),
            actorUserId: actorUserId,
            before: new { IsPublished = was },
            after: new { opinion.IsPublished });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("AppraisalOpinion {OpinionId} {Action} by {Actor}.",
            opinion.AppraisalOpinionId, publish ? "published" : "unpublished", actorUserId);

        return Result<AppraisalOpinionDto>.Success(ToDto(opinion));
    }

    public async Task<Result<IReadOnlyList<AppraisalOpinionDto>>> GetForProductAsync(
        Guid productId, bool includeUnpublished = false, CancellationToken ct = default)
    {
        var query = _db.AppraisalOpinions.AsNoTracking().Where(a => a.ProductId == productId);
        if (!includeUnpublished)
            query = query.Where(a => a.IsPublished);

        var rows = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(ct);

        IReadOnlyList<AppraisalOpinionDto> dtos = rows.Select(ToDto).ToList();
        return Result<IReadOnlyList<AppraisalOpinionDto>>.Success(dtos);
    }

    /// <summary>
    /// Current disclaimer version from ConfigVersions (key <see cref="DisclaimerConfigKey"/>). Falls back to
    /// <see cref="DefaultDisclaimerVersion"/> if unset; either way the value is truncated to the column's
    /// varchar(20) so the insert never fails on an over-long admin-entered label.
    /// </summary>
    private async Task<string> ResolveDisclaimerVersionAsync(DateTime asOfUtc, CancellationToken ct)
    {
        var raw = await _config.GetValueAsync(DisclaimerConfigKey, asOfUtc, ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning(
                "No disclaimer version configured (ConfigVersions key '{Key}'); stamping default '{Default}'.",
                DisclaimerConfigKey, DefaultDisclaimerVersion);
            return DefaultDisclaimerVersion;
        }

        var value = raw.Trim();
        return value.Length > 20 ? value[..20] : value;
    }

    private static bool TryFindForbiddenPhrase(string text, out string phrase)
    {
        var lowered = text.ToLowerInvariant();
        foreach (var p in ForbiddenPhrases)
        {
            if (lowered.Contains(p.ToLowerInvariant(), StringComparison.Ordinal))
            {
                phrase = p;
                return true;
            }
        }
        phrase = string.Empty;
        return false;
    }

    private static AppraisalOpinionDto ToDto(AppraisalOpinion a) => new(
        a.AppraisalOpinionId,
        a.ProductId,
        a.AppraiserUserId,
        a.AppraiserName,
        a.OpinionText,
        a.DisclaimerVersion,
        a.IsPublished,
        a.CreatedAtUtc);
}
