using Marketplace.Application.Catalog;
using Marketplace.Application.Common;

namespace Marketplace.Infrastructure.Services.Stubs;

// PHASE A PLACEHOLDER (M6). backend-dev(M6) replaces with a real AppraisalService in their own file and
// updates ModuleRegistration.AddM6Appraisal. DO NOT add business logic here.
internal sealed class M6AppraisalServiceStub : IAppraisalService
{
    public Task<Result<AppraisalOpinionDto>> CreateAsync(CreateAppraisalRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M6: IAppraisalService not yet implemented (Phase A stub).");

    public Task<Result<AppraisalOpinionDto>> PublishAsync(Guid appraisalOpinionId, Guid actorUserId, CancellationToken ct = default)
        => throw new NotImplementedException("M6: IAppraisalService not yet implemented (Phase A stub).");
}
