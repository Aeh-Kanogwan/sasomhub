using System.ComponentModel.DataAnnotations;
using Marketplace.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace Marketplace.Web.Models.Catalog;

/// <summary>
/// FR-06: create-listing form (requires ACTIVE membership — gated by [Authorize(Policy="MembershipActive")]).
/// FR-30: optional credit-paid promotion packages selected at publish time.
/// The standard intermediary disclaimer + "ข้อมูลถูกต้อง" confirmation are rendered/required in the view.
/// </summary>
public class CreateListingViewModel
{
    /// <summary>
    /// FR-06: card photos uploaded at create time (front/back/PSA seal). The form is multipart/form-data.
    /// ADDED by frontend-dev — the original VM had no image field (noted by lead-dev). Recommend requiring
    /// at least one image server-side (kept optional here so the Draft-save path stays flexible).
    /// TODO: backend-dev — accept these in the POST action, validate type/size (PNG/JPG, ≤8MB each),
    /// persist to storage + ProductImages, and reject unsupported types. The platform never alters the
    /// listing's authenticity claims (no-touch / no guarantee, FR-07).
    /// </summary>
    public IReadOnlyList<IFormFile> Images { get; set; } = new List<IFormFile>();

    /// <summary>Already-persisted image URLs (e.g. when re-rendering after a validation error / editing a draft).</summary>
    public IReadOnlyList<string> ExistingImageUrls { get; set; } = new List<string>();

    [Required, StringLength(140)]
    public string Title { get; set; } = string.Empty;

    [StringLength(120)]
    public string? SetLabel { get; set; }

    /// <summary>common | rare | epic | legendary.</summary>
    public string Rarity { get; set; } = "common";

    /// <summary>Grade label (appraiser opinion, e.g. "PSA 10"). Not a platform guarantee (FR-07).</summary>
    public string? GradeLabel { get; set; }

    public string? SerialLabel { get; set; }

    public ListingType ListingType { get; set; } = ListingType.FixedPrice;

    /// <summary>Asking price (FixedPrice). The platform never holds this money (no-touch, FR-18).</summary>
    [Range(0, 999_999_999)]
    public decimal? FixedPrice { get; set; }

    public int? CategoryId { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "ต้องยืนยันความถูกต้องของข้อมูลและยอมรับเงื่อนไข")]
    public bool ConfirmAccuracy { get; set; }

    /// <summary>FR-30: selected PromotionPackage codes (e.g. FEAT_7, TOP_3, HL_7) to pay with credit.</summary>
    public IReadOnlyList<byte> SelectedPromotionPackageIds { get; set; } = new List<byte>();

    /// <summary>Packages available for selection (seeded PromotionPackages: Featured/TopOfList/Highlight).</summary>
    public IReadOnlyList<PromotionPackageOption> PromotionOptions { get; set; } = new List<PromotionPackageOption>();
}

public record PromotionPackageOption(byte PromotionPackageId, string Label, PromotionType Type, int DurationDays, decimal CreditCost);
