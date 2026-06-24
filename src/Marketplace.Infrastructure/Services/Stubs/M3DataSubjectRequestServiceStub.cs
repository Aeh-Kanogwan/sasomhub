using Marketplace.Application.Common;
using Marketplace.Application.Privacy;

namespace Marketplace.Infrastructure.Services.Stubs;

// PHASE A PLACEHOLDER (M3). backend-dev(M3) replaces with a real DataSubjectRequestService in their
// own file and updates ModuleRegistration.AddM3Privacy. DO NOT add business logic here.
internal sealed class M3DataSubjectRequestServiceStub : IDataSubjectRequestService
{
    public Task<Result<DataSubjectRequestDto>> CreateAsync(CreateDsarRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M3: IDataSubjectRequestService not yet implemented (Phase A stub).");

    public Task<Result<IReadOnlyList<DataSubjectRequestDto>>> GetMyRequestsAsync(Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException("M3: IDataSubjectRequestService not yet implemented (Phase A stub).");

    public Task<Result<IReadOnlyList<DataSubjectRequestDto>>> GetForAdminAsync(string? status, CancellationToken ct = default)
        => throw new NotImplementedException("M3: IDataSubjectRequestService not yet implemented (Phase A stub).");

    public Task<Result<DataSubjectRequestDto>> AssignAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default)
        => throw new NotImplementedException("M3: IDataSubjectRequestService not yet implemented (Phase A stub).");

    public Task<Result<DataSubjectRequestDto>> FulfilExportAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default)
        => throw new NotImplementedException("M3: IDataSubjectRequestService not yet implemented (Phase A stub).");

    public Task<Result<DataSubjectRequestDto>> FulfilErasureAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default)
        => throw new NotImplementedException("M3: IDataSubjectRequestService not yet implemented (Phase A stub).");

    public Task<Result<DataSubjectRequestDto>> RejectAsync(Guid requestId, Guid handledByUserId, string reason, CancellationToken ct = default)
        => throw new NotImplementedException("M3: IDataSubjectRequestService not yet implemented (Phase A stub).");
}
