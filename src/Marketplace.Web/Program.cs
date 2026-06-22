using Marketplace.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----
// Antiforgery (CSRF) is on by default for the cookie-auth MVC site. Forms MUST include the
// token (Razor <form> tags emit it automatically) and POST actions should be guarded with
// [ValidateAntiForgeryToken] (TODO: backend-dev / or apply AutoValidateAntiforgeryTokenAttribute globally).
builder.Services.AddControllersWithViews(options =>
{
    // Defence-in-depth: validate the antiforgery token on every unsafe (POST/PUT/DELETE) request
    // so a controller author can never forget [ValidateAntiForgeryToken].
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

// Harden the antiforgery cookie itself (CSRF token cookie).
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // becomes Always behind HTTPS/HSTS in prod
});

// EF Core DbContext + infrastructure (connection string from appsettings).
// Reuses the same AddInfrastructure() registration as Marketplace.Api / Marketplace.Worker.
builder.Services.AddInfrastructure(builder.Configuration);

// Application services (IMembershipService / ICreditService / IReferralService / IAuctionService /
// ITradeService / ITrustScoreService) + the IPasswordHasher (NFR-S1) are all registered by
// AddInfrastructure() above so Api/Web/Worker share one composition root. Nothing to register here.

// Cookie authentication for the server-rendered site (NFR-S1). The API uses JWT; the MVC
// site uses a cookie issued on login. Real password hashing + sign-in lives in AccountController
// (TODO: backend-dev) — this only configures the scheme/handler.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization(options =>
{
    // FR-06/FR-10 gate: listing/bidding requires an ACTIVE membership (Trial or paid).
    // The "MembershipActive" claim is set at sign-in / refreshed by AccountController (TODO: backend-dev).
    // Guests can browse/search only (FR-08).
    options.AddPolicy("MembershipActive", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("MembershipActive", "true"));
});

var app = builder.Build();

// DEV-ONLY: ensure an Admin login exists for the Flow B dashboard (admin@neonvault.test / Admin@12345).
// Never runs in Production. Best-effort: a DB hiccup here must not block app startup.
if (app.Environment.IsDevelopment())
{
    try
    {
        await Marketplace.Web.DevAdminSeeder.SeedAsync(app.Services);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Dev admin seeding skipped (DB not ready?).");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
