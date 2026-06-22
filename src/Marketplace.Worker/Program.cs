using Marketplace.Infrastructure;
using Marketplace.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// EF Core DbContext + infrastructure (shares connection string config with Api).
builder.Services.AddInfrastructure(builder.Configuration);

// Background services (FR-12 auction close, FR-14/15 trust recalc, FR-27 trial/membership expiry,
// FR-32 notification dispatch, FR-28/29 credit expiry, FR-30 listing-promotion expiry).
builder.Services.AddHostedService<AuctionCloserService>();
builder.Services.AddHostedService<TrustScoreRecalcService>();
builder.Services.AddHostedService<MembershipTrialExpiryService>();
builder.Services.AddHostedService<NotificationDispatchService>();      // FR-27/FR-32 advance-notice 14/3/1d + delivery
builder.Services.AddHostedService<CreditExpiryService>();              // FR-28/FR-29 expire past-due credit lots
builder.Services.AddHostedService<ListingPromotionExpiryService>();   // FR-30 expire lapsed listing promotions

var host = builder.Build();
host.Run();
