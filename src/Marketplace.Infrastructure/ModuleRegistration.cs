using Marketplace.Application.Catalog;
using Marketplace.Application.Configuration;
using Marketplace.Application.Disputes;
using Marketplace.Application.Kyc;
using Marketplace.Application.Notifications;
using Marketplace.Application.Privacy;
using Marketplace.Application.Reputation;
using Marketplace.Infrastructure.Services.Kyc;
using Marketplace.Infrastructure.Services.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    internal static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddM1Notifications(configuration)
            .AddM2Kyc(configuration)
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
    private static IServiceCollection AddM1Notifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Read "Notifications" config (provider mode + SMTP/SendGrid/SMS placeholders + PDPA pepper)
        // via the IConfiguration indexer (no Microsoft.Extensions.Configuration.Binder dependency).
        // Defaults are Log mode so Api/Web/Worker boot with NO real credentials.
        var options = NotificationOptions.FromConfiguration(
            configuration.GetSection(NotificationOptions.SectionName));
        services.AddSingleton(options);

        // One shared HttpClient for the HTTP-based providers (SendGrid email / SMS gateway). We avoid
        // adding Microsoft.Extensions.Http (IHttpClientFactory) to keep the dependency set unchanged;
        // a single long-lived HttpClient is the recommended pattern for low-volume outbound calls.
        services.AddSingleton<NotificationHttpClient>();

        // IEmailSender: pick transport by config (Smtp | SendGrid | Log dev-fallback).
        services.AddScoped<IEmailSender>(sp => options.EmailProvider switch
        {
            EmailProvider.Smtp => new SmtpEmailSender(options, sp.GetRequiredService<ILogger<SmtpEmailSender>>()),
            EmailProvider.SendGrid => new SendGridEmailSender(
                sp.GetRequiredService<NotificationHttpClient>().Client, options,
                sp.GetRequiredService<ILogger<SendGridEmailSender>>()),
            _ => new LogEmailSender(sp.GetRequiredService<ILogger<LogEmailSender>>()),
        });

        // ISmsSender: HttpGateway | Log dev-fallback.
        services.AddScoped<ISmsSender>(sp => options.SmsProvider switch
        {
            SmsProvider.HttpGateway => new SmsHttpSender(
                sp.GetRequiredService<NotificationHttpClient>().Client, options,
                sp.GetRequiredService<ILogger<SmsHttpSender>>()),
            _ => new LogSmsSender(sp.GetRequiredService<ILogger<LogSmsSender>>()),
        });

        // Facade: routes + renders + PERSISTS the NotificationDeliveryLog (LEGAL #8) on every send.
        services.AddScoped<INotificationSender, NotificationSender>();
        return services;
    }

    // ---- M2: KYC / NDID provider abstraction + mock mode ---------------------------------------
    // backend-dev(M2): IKycProvider chosen by Kyc:Mode (Mock dev/test default | Ndid real) + the real
    // KycService. LEGAL #2 startup hard-guard: Production may NOT run with Mode=Mock.
    private static IServiceCollection AddM2Kyc(this IServiceCollection services, IConfiguration configuration)
    {
        // Read the "Kyc" section. Default Mode=Mock so the solution boots in dev/test/CI with NO NDID
        // credential. Mode is normalised case-insensitively to Mock | Ndid. (Uses the IConfiguration
        // indexer rather than .Bind() to avoid taking a new package dependency in Infrastructure.)
        var options = KycOptions.FromConfiguration(configuration.GetSection(KycOptions.SectionName));
        services.AddSingleton(options);

        var isNdid = string.Equals(options.Mode, KycMode.Ndid, StringComparison.OrdinalIgnoreCase);

        // LEGAL #2 hard-guard: refuse to boot a Production host on the Mock provider. Environment is read
        // from ASPNETCORE_ENVIRONMENT (the standard host env var) so we avoid a dependency on the hosting
        // abstractions here. CI/dev/test leave this unset => Mock is allowed.
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase) && !isNdid)
        {
            throw new InvalidOperationException(
                "KYC misconfiguration: Kyc:Mode=Mock is not permitted in Production (LEGAL #2). " +
                "Set Kyc:Mode=Ndid and supply the NDID credential/endpoint via the secret store.");
        }

        if (isNdid)
        {
            // Real NDID transport over a single shared HttpClient (no IHttpClientFactory dependency).
            services.AddSingleton<KycHttpClient>();
            services.AddScoped<IKycProvider>(sp =>
                new NdidKycProvider(sp.GetRequiredService<KycHttpClient>().Client, options));
        }
        else
        {
            // Sandbox provider — no network, always verifies, tagged MOCK.
            services.AddScoped<IKycProvider, MockKycProvider>();
        }

        services.AddScoped<IKycService, KycService>();
        return services;
    }

    // ---- M3: PDPA DSAR -------------------------------------------------------------------------
    // FR-01/04: export/erasure/access/rectify/withdraw-consent requests + admin handling. Export
    // artifacts go through IDsarExportStore (local filesystem by default; configurable root).
    private static IServiceCollection AddM3Privacy(this IServiceCollection services)
    {
        services.AddScoped<Services.Privacy.IDsarExportStore, Services.Privacy.FileSystemDsarExportStore>();
        services.AddScoped<IDataSubjectRequestService, Services.Privacy.DataSubjectRequestService>();
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
