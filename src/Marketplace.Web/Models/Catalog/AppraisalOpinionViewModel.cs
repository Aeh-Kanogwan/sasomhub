namespace Marketplace.Web.Models.Catalog;

/// <summary>
/// FR-07/FR-34: an independent appraiser's opinion bound to a product.
/// IMPORTANT (Legal #2): OpinionText is a third-party opinion, NOT a platform guarantee.
/// The view MUST render the DisclaimerVersion text and MUST NOT contain the words
/// "รับประกัน" / "การันตี" / "ของแท้ 100%" (enforced server-side by backend-dev validation).
/// </summary>
public class AppraisalOpinionViewModel
{
    public Guid AppraisalOpinionId { get; init; }
    public string AppraiserName { get; init; } = string.Empty;
    public string OpinionText { get; init; } = string.Empty;
    public string DisclaimerVersion { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}
