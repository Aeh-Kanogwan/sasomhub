using Marketplace.Application.Configuration;

namespace Marketplace.Web.Models.AdminConfig;

/// <summary>M7 (FR-31) admin config overview — one row per key showing its current effective value.</summary>
public class AdminConfigIndexViewModel
{
    public IReadOnlyList<ConfigVersionDto> Current { get; init; } = new List<ConfigVersionDto>();
}

/// <summary>
/// M7 (FR-31) per-key history page: the append-only version timeline (newest first) plus the
/// "append a new version" form. The platform NEVER edits an existing row — each save adds a new one.
/// </summary>
public class AdminConfigHistoryViewModel
{
    public string ConfigKey { get; init; } = null!;
    public IReadOnlyList<ConfigVersionDto> History { get; init; } = new List<ConfigVersionDto>();

    /// <summary>Pre-filled value/effective time for the append form (sticky after a validation error).</summary>
    public string? NewValue { get; set; }
    public DateTime NewEffectiveFromUtc { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}
