using Marketplace.Application.Common;
using Marketplace.Application.Disputes;

namespace Marketplace.Infrastructure.Services.Stubs;

// PHASE A PLACEHOLDER (M4). backend-dev(M4) replaces with a real DisputeService in their own file and
// updates ModuleRegistration.AddM4Disputes. DO NOT add business logic here.
internal sealed class M4DisputeServiceStub : IDisputeService
{
    public Task<Result<DisputeDto>> RaiseAsync(RaiseDisputeRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M4: IDisputeService not yet implemented (Phase A stub).");

    public Task<Result<DisputeDto>> GetAsync(Guid disputeId, CancellationToken ct = default)
        => throw new NotImplementedException("M4: IDisputeService not yet implemented (Phase A stub).");

    public Task<Result<IReadOnlyList<DisputeDto>>> GetForUserAsync(Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException("M4: IDisputeService not yet implemented (Phase A stub).");

    public Task<Result<IReadOnlyList<DisputeDto>>> GetForAdminAsync(string? status, CancellationToken ct = default)
        => throw new NotImplementedException("M4: IDisputeService not yet implemented (Phase A stub).");

    public Task<Result<DisputeDto>> TransitionAsync(ResolveDisputeRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M4: IDisputeService not yet implemented (Phase A stub).");
}
