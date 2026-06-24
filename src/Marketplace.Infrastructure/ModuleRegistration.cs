using Marketplace.Application.Catalog;
using Marketplace.Application.Configuration;
using Marketplace.Application.Disputes;
using Marketplace.Application.Kyc;
using Marketplace.Application.Notifications;
using Marketplace.Application.Privacy;
using Marketplace.Application.Reputation;
using Marketplace.Infrastructure.Services.Stubs;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Infrastructure;

/// <summary>
/// PHASE A composition root for the parallel modules M1..M7. Called ONCE from
/// <see cref="DependencyInjection.AddInfrastructure"/> so Api / Web / Worker share identical wiring
/// (no host-specific DI duplication, no merge conflicts in the three Program.cs files).
///
/// CONTRACT FOR backend-dev: each module owns EXACTLY ONE method below (AddM1Notifications .. M7).
/// To go live, a module author:
///   1) creates their real service class(es) in their OWN new file under Services/ (never touching
///      another module's file),
///   2) edits ONLY their own AddMx method here to register the real impl instead of the *Stub,
///   3) deletes their Services/Stubs/Mx*Stub.cs when done.
/// Because each module touches a different method in this single file, two devs editing different
/// methods rarely textually conflict; if they do, it is a trivial, isolated 3-line merge.
/// All services are Scoped — they consume the scoped MarketplaceDbContext.
/// </summary>
public static class ModuleRegistration
{
    internal static IServiceCollection AddModules(this IServiceCollection services)
    {
        services
            .AddM1Notifications()
            .AddM2Kyc()
            .AddM3Privacy()
            .AddM4Disputes()
            .AddM5Blacklist()
            .AddM6Appraisal()
            .AddM7ConfigAdmin();
        return services;
    }

    // ---- M1: Email/SMS provider + delivery log -------------------------------------------------
    // backend-dev(M1): register real IEmailSender / ISmsSender (provider chosen by config) + the
    // INotificationSender facade that persists NotificationDeliveryLog. Replace the 3 stubs below.
    private static IServiceCollection AddM1Notifications(this IServiceCollection services)
    {
        services.AddScoped<IEmailSender, M1EmailSenderStub>();
        services.AddScoped<ISmsSender, M1SmsSenderStub>();
        services.AddScoped<INotificationSender, M1NotificationSenderStub>();
        return services;
    }

    // ---- M2: KYC / NDID provider abstraction + mock mode ---------------------------------------
    // backend-dev(M2): register the IKycProvider impl(s) (NDID sandbox + Mock, selected by config) and
    // the real IKycService. Replace the 2 stubs below.
    private static IServiceCollection AddM2Kyc(this IServiceCollection services)
    {
        services.AddScoped<IKycProvider, M2KycProviderStub>();
        services.AddScoped<IKycService, M2KycServiceStub>();
        return services;
    }

    // ---- M3: PDPA DSAR -------------------------------------------------------------------------
    private static IServiceCollection AddM3Privacy(this IServiceCollection services)
    {
        services.AddScoped<IDataSubjectRequestService, M3DataSubjectRequestServiceStub>();
        return services;
    }

    // ---- M4: Dispute service + flow ------------------------------------------------------------
    private static IServiceCollection AddM4Disputes(this IServiceCollection services)
    {
        services.AddScoped<IDisputeService, Services.Disputes.DisputeService>();
        return services;
    }

    // ---- M5: Blacklist warn-on-deal + admin ban/unban ------------------------------------------
    private static IServiceCollection AddM5Blacklist(this IServiceCollection services)
    {
        services.AddScoped<IBlacklistService, Services.Reputation.BlacklistService>();
        return services;
    }

    // ---- M6: AppraisalOpinion create -----------------------------------------------------------
    // FR-07 / LEGAL #2: real write service for dbo.AppraisalOpinions (opinion only, stamps the current
    // disclaimer version, rejects warranty wording). Depends on ConfigVersionResolver (registered in
    // DependencyInjection) + the scoped IAuditService.
    private static IServiceCollection AddM6Appraisal(this IServiceCollection services)
    {
        services.AddScoped<IAppraisalService, Services.Catalog.AppraisalService>();
        return services;
    }

    // ---- M7: Config versioning admin edit ------------------------------------------------------
    private static IServiceCollection AddM7ConfigAdmin(this IServiceCollection services)
    {
        // Real append-only config-version admin service (FR-31). Reuses the already-registered
        // ConfigVersionResolver for "current effective value" reads; every Set APPENDs an immutable row.
        services.AddScoped<IConfigAdminService, Services.Configuration.ConfigAdminService>();
        return services;
    }
}
