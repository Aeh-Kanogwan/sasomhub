using Marketplace.Application.Common;
using Marketplace.Application.Kyc;

namespace Marketplace.Infrastructure.Services.Stubs;

// PHASE A PLACEHOLDER (M2). backend-dev(M2) replaces with a real KycService + an IKycProvider impl
// (NDID sandbox + a Mock provider selected by config), in their own files, and updates
// ModuleRegistration.AddM2Kyc. DO NOT add business logic here.

internal sealed class M2KycServiceStub : IKycService
{
    public Task<Result<StartKycResult>> StartVerificationAsync(StartKycRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M2: IKycService not yet implemented (Phase A stub).");

    public Task<Result<KycStatusDto>> HandleCallbackAsync(KycCallback callback, CancellationToken ct = default)
        => throw new NotImplementedException("M2: IKycService not yet implemented (Phase A stub).");

    public Task<Result<KycStatusDto?>> GetStatusAsync(Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException("M2: IKycService not yet implemented (Phase A stub).");
}

internal sealed class M2KycProviderStub : IKycProvider
{
    public string ProviderKey => "Mock";

    public Task<StartKycResult> InitiateAsync(StartKycRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M2: IKycProvider not yet implemented (Phase A stub).");

    public Task<KycCallback> CheckStatusAsync(string providerReference, CancellationToken ct = default)
        => throw new NotImplementedException("M2: IKycProvider not yet implemented (Phase A stub).");
}
