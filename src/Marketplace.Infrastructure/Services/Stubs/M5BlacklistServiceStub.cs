using Marketplace.Application.Common;
using Marketplace.Application.Reputation;

namespace Marketplace.Infrastructure.Services.Stubs;

// PHASE A PLACEHOLDER (M5). backend-dev(M5) replaces with a real BlacklistService in their own file and
// updates ModuleRegistration.AddM5Blacklist. DO NOT add business logic here.
internal sealed class M5BlacklistServiceStub : IBlacklistService
{
    public Task<Result<IReadOnlyList<BlacklistWarning>>> GetActiveWarningsAsync(Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException("M5: IBlacklistService not yet implemented (Phase A stub).");

    public Task<Result<IReadOnlyList<BlacklistEntryDto>>> GetForAdminAsync(string? reviewStatus, CancellationToken ct = default)
        => throw new NotImplementedException("M5: IBlacklistService not yet implemented (Phase A stub).");

    public Task<Result<BlacklistEntryDto>> ProposeAsync(ProposeBlacklistRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M5: IBlacklistService not yet implemented (Phase A stub).");

    public Task<Result<BlacklistEntryDto>> ReviewAsync(ReviewBlacklistRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M5: IBlacklistService not yet implemented (Phase A stub).");

    public Task<Result<BlacklistEntryDto>> UnbanAsync(Guid blacklistEntryId, Guid reviewedByUserId, string? note, CancellationToken ct = default)
        => throw new NotImplementedException("M5: IBlacklistService not yet implemented (Phase A stub).");
}
