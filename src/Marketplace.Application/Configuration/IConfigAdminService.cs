using Marketplace.Application.Common;

namespace Marketplace.Application.Configuration;

// M7: admin read/edit of versioned config (FR-31, B-04/G-4). Every change APPENDS a new ConfigVersion
// row with a future/now EffectiveFromUtc — NEVER updates an existing row (immutable history; the DB
// unique index UQ_ConfigVer_KeyEffective forbids two versions at the same instant). Resolution at
// runtime stays with the existing ConfigVersionResolver ("latest EffectiveFromUtc <= now"). Audited.

/// <summary>Append a new effective value for a config key (the edit operation in FR-31).</summary>
public record SetConfigRequest(string ConfigKey, string Value, DateTime EffectiveFromUtc, Guid ChangedByUserId, string? Note);

public record ConfigVersionDto(
    long ConfigVersionId,
    string ConfigKey,
    string Value,
    DateTime EffectiveFromUtc,
    Guid? CreatedByUserId,
    string? Note,
    DateTime CreatedAtUtc);

/// <summary>M7 admin config service (FR-31). Read history + append new versions; never mutate existing.</summary>
public interface IConfigAdminService
{
    /// <summary>List the distinct config keys with their CURRENT effective value (admin overview).</summary>
    Task<Result<IReadOnlyList<ConfigVersionDto>>> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Full append-only version history for one key (newest first).</summary>
    Task<Result<IReadOnlyList<ConfigVersionDto>>> GetHistoryAsync(string configKey, CancellationToken ct = default);

    /// <summary>
    /// FR-31: append a new version (immutable). Rejects an EffectiveFromUtc that collides with an
    /// existing version for the key. Does NOT apply retroactively to already-charged cycles.
    /// </summary>
    Task<Result<ConfigVersionDto>> SetAsync(SetConfigRequest request, CancellationToken ct = default);
}
