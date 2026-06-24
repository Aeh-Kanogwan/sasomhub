using Marketplace.Application.Disputes;

namespace Marketplace.Web.Models.Disputes;

/// <summary>A single reason-code option for the raise-dispute dropdown (standard codes, Legal #3).</summary>
public record ReasonCodeOption(int ReasonCodeId, string DisplayName);

/// <summary>User's own dispute list (raised by them or on their transactions).</summary>
public class MyDisputesViewModel
{
    public IReadOnlyList<DisputeDto> Disputes { get; init; } = new List<DisputeDto>();
}

/// <summary>Raise-dispute form for a transaction the current user is a party to (FR-21).</summary>
public class RaiseDisputeViewModel
{
    public Guid TransactionId { get; init; }
    public int ReasonCodeId { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<ReasonCodeOption> ReasonCodes { get; init; } = new List<ReasonCodeOption>();
}

/// <summary>Single dispute detail (parties + admin read).</summary>
public class DisputeDetailViewModel
{
    public DisputeDto Dispute { get; init; } = null!;
}

/// <summary>Admin dispute queue, filtered by status (NULL = all).</summary>
public class AdminDisputesViewModel
{
    public string? StatusFilter { get; init; }
    public IReadOnlyList<DisputeDto> Disputes { get; init; } = new List<DisputeDto>();
}

/// <summary>Admin single-dispute review page (transition: UnderReview/Resolved/Rejected/Escalated).</summary>
public class AdminDisputeDetailViewModel
{
    public DisputeDto Dispute { get; init; } = null!;
}
