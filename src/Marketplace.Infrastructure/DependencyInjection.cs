using Marketplace.Application.Auctions;
using Marketplace.Application.Credits;
using Marketplace.Application.Memberships;
using Marketplace.Application.Referrals;
using Marketplace.Application.Reputation;
using Marketplace.Application.Trades;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Infrastructure;

/// <summary>Registers EF Core DbContext + infrastructure services. Called from Api and Worker hosts.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Connection string resolution order (highest precedence first), centralized so Api/Web/Worker
        // and the design-time factory all agree:
        //   1) MARKETPLACE_CONNECTION env var  (CI/CD / container / per-environment override; never in repo)
        //   2) ConnectionStrings:MarketplaceDb (user-secrets > appsettings.{Env}.json > appsettings.json)
        // The appsettings value is the LocalDB dev default only; production injects #1.
        var connectionString = Environment.GetEnvironmentVariable("MARKETPLACE_CONNECTION")
            ?? configuration.GetConnectionString("MarketplaceDb")
            ?? throw new InvalidOperationException(
                "No connection string. Set the MARKETPLACE_CONNECTION env var or ConnectionStrings:MarketplaceDb.");

        services.AddDbContext<MarketplaceDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(MarketplaceDbContext).Assembly.FullName)));

        // Membership / Credit / Referral services (FR-05/22/27/28/29/30). Scoped: they depend on the
        // scoped DbContext. NOTE: the parallel Auction/Trade/Trust services are registered elsewhere.
        services.AddScoped<ConfigVersionResolver>();           // B-04/G-4 config-version price resolver
        // NFR-S1: one-way password hashing (PBKDF2, BCL-only). Stateless -> singleton.
        services.AddSingleton<Marketplace.Application.Common.IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IMembershipService, MembershipService>();
        // Flow B membership payment: slip upload + Admin confirm (company money; no-touch preserved).
        services.AddScoped<Marketplace.Application.Payments.IPaymentSlipService, PaymentSlipService>();
        services.AddScoped<ICreditService, CreditService>();
        services.AddScoped<IReferralService, ReferralService>();
        // FR-28: per-user single-level referral code generator (unique, CSPRNG, DB-checked). Scoped: uses DbContext.
        services.AddScoped<IReferralCodeGenerator, ReferralCodeGenerator>();

        // Auction / Bidding (FR-09..12), no-touch Trade (FR-18/19), Trust Score (FR-13..16).
        // Scoped: depend on the scoped DbContext; also consumed by the Worker background services.
        services.AddScoped<ITrustScoreService, TrustScoreService>();
        services.AddScoped<ITradeService, TradeService>();   // depends on ITrustScoreService
        services.AddScoped<IAuctionService, AuctionService>(); // depends on ITradeService

        // TODO: register repositories / unit-of-work / remaining domain services here.
        return services;
    }
}
