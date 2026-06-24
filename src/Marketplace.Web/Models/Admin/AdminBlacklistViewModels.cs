using Marketplace.Application.Reputation;

namespace Marketplace.Web.Models.Admin;

/// <summary>
/// M5 — Admin blacklist queue (FR-17). Lists pending-ban-review proposals + existing entries by
/// review status (NULL = all). Standard reason codes only (Legal #3) — no free text rendered to
/// non-admins; the admin surface may show the internal note as it is the human reviewer.
/// </summary>
public class AdminBlacklistViewModel
{
    public string? StatusFilter { get; init; }
    public IReadOnlyList<BlacklistEntryDto> Entries { get; init; } = new List<BlacklistEntryDto>();
}
