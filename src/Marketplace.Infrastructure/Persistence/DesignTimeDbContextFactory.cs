using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef` at design time (migrations) when the app host is not available.
/// The connection string here is only for tooling; runtime DI provides the real one.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETPLACE_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=MarketplaceDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(MarketplaceDbContext).Assembly.FullName))
            .Options;

        return new MarketplaceDbContext(options);
    }
}
