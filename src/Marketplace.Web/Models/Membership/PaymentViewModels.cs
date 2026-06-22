using System.ComponentModel.DataAnnotations;
using Marketplace.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace Marketplace.Web.Models.Membership;

/// <summary>
/// Flow B (FR-22/FR-35): the "pay a pending membership invoice by bank transfer + slip upload" page.
/// Shows the amount due, the company bank account (from ConfigVersions), and the slip-upload form.
/// Membership fee is COMPANY money — separate from buyer↔seller trade money (no-touch preserved).
/// </summary>
public class PayInvoiceViewModel
{
    public Guid FeeInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "THB";
    public FeeType FeeType { get; set; }
    public FeeInvoiceStatus InvoiceStatus { get; set; }
    public DateTime IssuedAtUtc { get; set; }

    /// <summary>Company bank account details resolved from ConfigVersions (placeholders until business sets them).</summary>
    public CompanyBankInfo Bank { get; set; } = new();

    /// <summary>The most recent slip the member submitted for this invoice (null if none yet).</summary>
    public SubmittedSlipInfo? ExistingSlip { get; set; }

    // ---- form fields ----

    [Display(Name = "ยอดที่โอน (บาท)")]
    [Range(0, 10_000_000, ErrorMessage = "ยอดที่โอนไม่ถูกต้อง")]
    public decimal AmountClaimed { get; set; }

    [Display(Name = "วันเวลาที่โอน")]
    [Required(ErrorMessage = "กรุณาระบุวันเวลาที่โอน")]
    [DataType(DataType.DateTime)]
    public DateTime TransferredAt { get; set; } = DateTime.Now;

    [Display(Name = "หมายเหตุ / เลขอ้างอิงจากสลิป")]
    [StringLength(200)]
    public string? BankRefNote { get; set; }

    [Display(Name = "รูปสลิป (PNG/JPG ≤ 8MB)")]
    public IFormFile? SlipImage { get; set; }
}

public class CompanyBankInfo
{
    public string BankName { get; set; } = string.Empty;
    public string AccountNo { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? PromptPayId { get; set; }
}

public class SubmittedSlipInfo
{
    public Guid PaymentSlipId { get; set; }
    public PaymentSlipStatus Status { get; set; }
    public decimal AmountClaimed { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public string SlipImageUrl { get; set; } = string.Empty;
}
