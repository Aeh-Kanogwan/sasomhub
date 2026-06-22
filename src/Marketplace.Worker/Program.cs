using Marketplace.Infrastructure;
using Marketplace.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// EF Core DbContext + infrastructure (shares connection string config with Api).
builder.Services.AddInfrastructure(builder.Configuration);

// Background services (FR-12 auction close, FR-14/15 trust recalc, FR-27 trial/membership expiry).
builder.Services.AddHostedService<AuctionCloserService>();
builder.Services.AddHostedService<TrustScoreRecalcService>();
builder.Services.AddHostedService<MembershipTrialExpiryService>();
builder.Services.AddHostedService<NotificationDispatchService>(); // FR-27/B-01 advance-notice 14/3/1d
// TODO: add CreditExpiryService + ListingPromotionExpiryService (FR-28/FR-30) later.

var host = builder.Build();
host.Run();
