using System.Text;
using Marketplace.Application.Common;
using Marketplace.Infrastructure;
using Marketplace.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// RFC 7807 ProblemDetails for failed model validation + thrown exceptions, so every error path
// returns a consistent machine-readable body (the controllers also return ProblemDetails explicitly).
builder.Services.AddProblemDetails();

// EF Core DbContext + Application services (IMembershipService, ICreditService, IReferralService,
// IAuctionService, ITradeService, ITrustScoreService, IPaymentSlipService, IPasswordHasher) are all
// registered here by AddInfrastructure — we only add the JWT token service (this phase) below.
builder.Services.AddInfrastructure(builder.Configuration);

// JWT auth (NFR-S1). The signing key MUST come from a secret store, never appsettings.json:
//   dev   -> dotnet user-secrets ("Jwt:SigningKey")
//   prod  -> env var / key vault  (Jwt__SigningKey)
// We fail fast outside Development if the key is missing/placeholder/too short, so a build can
// never accidentally ship with a guessable HS256 key (token-forgery risk).
var jwt = builder.Configuration.GetSection("Jwt");
const string DevPlaceholderKey = "DEV-ONLY-INSECURE-SIGNING-KEY-32+chars-CHANGE-ME";
var placeholderMarkers = new[] { "REPLACE", "PLACEHOLDER", "CHANGE-ME", "CHANGE_ME" };
var signingKey = jwt["SigningKey"];
var isMissingOrPlaceholder = string.IsNullOrWhiteSpace(signingKey)
    || signingKey.Length < 32
    || placeholderMarkers.Any(m => signingKey.Contains(m, StringComparison.OrdinalIgnoreCase));

if (isMissingOrPlaceholder)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey is missing, a placeholder, or shorter than 32 chars. " +
            "Provide a real >=32-char secret via env var Jwt__SigningKey or a key vault before deploying.");
    }
    // Development only: fall back to a fixed dev key so the API still boots locally.
    signingKey = DevPlaceholderKey;
}

// JWT issuance settings — one source of truth for the resolved key + issuer/audience/lifetimes.
// JwtTokenService gets the key AFTER the fail-fast check above, so it never re-reads the secret.
var jwtOptions = new JwtOptions
{
    SigningKey = signingKey!,
    Issuer = jwt["Issuer"] ?? "marketplace.local",
    Audience = jwt["Audience"] ?? "marketplace.local",
    AccessTokenMinutes = int.TryParse(jwt["AccessTokenMinutes"], out var am) ? am : 30,
    RefreshTokenDays = int.TryParse(jwt["RefreshTokenDays"], out var rd) ? rd : 14,
};
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey!)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// NFR-S3 RBAC + FR-06/FR-10 gate policies.
//   "Admin"            -> role claim == Admin (admin-only endpoints).
//   "MembershipActive" -> MembershipActive claim == true (create listing / place bid / promote).
// The membership claim is a snapshot taken at token-issue/refresh time; controllers that must be
// strict re-verify against IMembershipService before the write (a token can outlive a membership).
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", p => p.RequireRole(nameof(Marketplace.Domain.Enums.UserRole.Admin)));
    options.AddPolicy("MembershipActive", p =>
        p.RequireClaim(JwtTokenService.MembershipActiveClaim, "true"));
});

// Swagger + JWT bearer support.
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Marketplace API (MVP)", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
