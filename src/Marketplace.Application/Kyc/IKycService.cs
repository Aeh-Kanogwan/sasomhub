using Marketplace.Application.Common;

namespace Marketplace.Application.Kyc;

// M2: e-KYC via NDID. Two-layer abstraction:
//   IKycProvider  = the external identity-proofing transport (NDID sandbox / mock / real).
//   IKycService   = orchestration: starts a verification, persists KycVerification + KycSensitiveData,
//                   advances KycStatus (NONE -> INITIATED -> PENDING -> VERIFIED/REJECTED/EXPIRED),
//                   and gates Verified/Premium membership tiers (FR-02/03, MembershipTier.RequiresKyc).
// LEGAL #4 / FR-02/03: NEVER store raw ID images/numbers — only status + provider ref + masked/hashed
// sensitive fields in dbo.KycSensitiveData with a retention boundary.

/// <summary>Target verification level (maps to MembershipTier.RequiresKyc — Verified/Premium).</summary>
public enum KycLevel { Verified = 2, Premium = 3 }

/// <summary>Start an e-KYC session for a user (returns a redirect/handoff URL for NDID).</summary>
public record StartKycRequest(Guid UserId, KycLevel TargetLevel);

/// <summary>What the caller needs to hand the user off to the provider (NDID redirect/QR).</summary>
public record StartKycResult(Guid KycVerificationId, string Provider, string? RedirectUrl, string? ProviderReference);

/// <summary>Async provider callback payload (verified/rejected) reconciled back to a session.</summary>
public record KycCallback(string ProviderReference, bool Verified, string? FullNameMasked, byte[]? NationalIdHash, string? RawStatus);

public record KycStatusDto(Guid KycVerificationId, Guid UserId, string StatusCode, byte VerificationLevel, DateTime? VerifiedAtUtc, DateTime? ExpiresAtUtc);

/// <summary>
/// M2: provider transport abstraction. A mock/sandbox implementation lets the rest of the system run
/// without a live NDID connection (config flag selects Mock vs Ndid). Pure transport — no DB access.
/// </summary>
public interface IKycProvider
{
    /// <summary>Provider key persisted on KycVerification.Provider (e.g. 'NDID','NdidSandbox','Mock').</summary>
    string ProviderKey { get; }

    /// <summary>Begin an identity-proofing request; returns a handoff (redirect/QR) + provider reference.</summary>
    Task<StartKycResult> InitiateAsync(StartKycRequest request, CancellationToken ct = default);

    /// <summary>Poll/verify a provider reference (used when no async webhook is available, e.g. mock mode).</summary>
    Task<KycCallback> CheckStatusAsync(string providerReference, CancellationToken ct = default);
}

/// <summary>
/// M2: KYC orchestration service. Owns the KycVerification/KycSensitiveData rows and the KycStatus
/// state machine; writes AuditLogs on every transition. Consumed by Account/Membership flows.
/// </summary>
public interface IKycService
{
    /// <summary>FR-02: start e-KYC, create a KycVerification (status INITIATED) and return the provider handoff.</summary>
    Task<Result<StartKycResult>> StartVerificationAsync(StartKycRequest request, CancellationToken ct = default);

    /// <summary>FR-02/03: handle the provider callback/result — persist masked data, advance status, set expiry.</summary>
    Task<Result<KycStatusDto>> HandleCallbackAsync(KycCallback callback, CancellationToken ct = default);

    /// <summary>Read the user's current KYC status (drives Verified/Premium tier eligibility).</summary>
    Task<Result<KycStatusDto?>> GetStatusAsync(Guid userId, CancellationToken ct = default);
}
