using Marketplace.Application.Payments;

namespace Marketplace.Web.Models.Admin;

/// <summary>Admin dashboard summary counters.</summary>
public class AdminDashboardViewModel
{
    public int PendingSlipCount { get; init; }
    public int OpenDisputeCount { get; init; }
    public int PendingBanReviewCount { get; init; }
}

/// <summary>Admin payments queue: pending payment slips awaiting review.</summary>
public class AdminPaymentsViewModel
{
    public IReadOnlyList<PaymentSlipReviewDto> Pending { get; init; } = new List<PaymentSlipReviewDto>();
}

/// <summary>Admin single-slip review page (Approve/Reject).</summary>
public class AdminPaymentDetailViewModel
{
    public PaymentSlipReviewDto Slip { get; init; } = null!;
}
