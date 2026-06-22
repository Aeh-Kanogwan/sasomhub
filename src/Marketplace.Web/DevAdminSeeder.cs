using Marketplace.Application.Common;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Web;

/// <summary>
/// DEV-ONLY: ensures an Admin account exists so the Flow B admin dashboard can be exercised locally.
/// Guarded by IsDevelopment() at the call site — must NEVER run in Production (no hardcoded admin in prod).
/// </summary>
public static class DevAdminSeeder
{
    public const string AdminEmail = "admin@neonvault.test";
    private const string AdminPassword = "Admin@12345";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Idempotent: skip if any Admin already exists.
        var adminExists = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Role == UserRole.Admin && !u.IsDeleted, ct);
        if (adminExists) return;

        var normalizedEmail = AdminEmail.ToUpperInvariant();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsDeleted, ct);
        var now = DateTime.UtcNow;

        if (existing is not null)
        {
            // Promote the placeholder account to Admin (and (re)set the known dev password).
            existing.Role = UserRole.Admin;
            existing.AccountStatus = AccountStatus.Active;
            existing.PasswordHash = hasher.Hash(AdminPassword);
            existing.UpdatedAtUtc = now;
        }
        else
        {
            db.Users.Add(new User
            {
                Email = AdminEmail,
                NormalizedEmail = normalizedEmail,
                PasswordHash = hasher.Hash(AdminPassword),
                Role = UserRole.Admin,
                AccountStatus = AccountStatus.Active,
                EmailConfirmed = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Profile = new UserProfile { DisplayName = "Admin", UpdatedAtUtc = now },
                TrustScore = new TrustScore { Score = 100, LastCalculatedAtUtc = now },
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
