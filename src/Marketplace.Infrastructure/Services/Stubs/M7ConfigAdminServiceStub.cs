using Marketplace.Application.Common;
using Marketplace.Application.Configuration;

namespace Marketplace.Infrastructure.Services.Stubs;

// PHASE A PLACEHOLDER (M7). backend-dev(M7) replaces with a real ConfigAdminService in their own file and
// updates ModuleRegistration.AddM7ConfigAdmin. DO NOT add business logic here.
internal sealed class M7ConfigAdminServiceStub : IConfigAdminService
{
    public Task<Result<IReadOnlyList<ConfigVersionDto>>> GetCurrentAsync(CancellationToken ct = default)
        => throw new NotImplementedException("M7: IConfigAdminService not yet implemented (Phase A stub).");

    public Task<Result<IReadOnlyList<ConfigVersionDto>>> GetHistoryAsync(string configKey, CancellationToken ct = default)
        => throw new NotImplementedException("M7: IConfigAdminService not yet implemented (Phase A stub).");

    public Task<Result<ConfigVersionDto>> SetAsync(SetConfigRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M7: IConfigAdminService not yet implemented (Phase A stub).");
}
