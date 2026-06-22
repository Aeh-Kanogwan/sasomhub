using Marketplace.Application.Credits;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// FR-28/FR-29: expires credit lots whose ExpiresAtUtc has passed. Credit is append-only — expiry is
/// recorded by INSERTING a negative <c>Expiry</c> ledger row (never by editing the original grant),
/// and the cached balance is decremented in the same DB transaction (handled by CreditService).
///
/// Idempotency: each grant lot is expired at most once. The expiry row is keyed by RefId =
/// "CreditTx:{grantTransactionId}" with Type=Expiry, so the unique index UX_CreditTx_TypeRef rejects
/// a second expiry for the same lot — a re-run / crash-resume cannot double-deduct. The deducted
/// amount is clamped to the current balance so an already-spent lot never drives the balance below 0
/// (CK_CreditAcc_Balance).
/// </summary>
public class CreditExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int BatchSize = 200;
    private readonly IServiceProvider _services;
    private readonly ILogger<CreditExpiryService> _logger;

    public CreditExpiryService(IServiceProvider services, ILogger<CreditExpiryService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreditExpiry sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>One sweep: expire all past-due credit lots not yet expired. Idempotent. Returns lots expired.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var credit = scope.ServiceProvider.GetRequiredService<ICreditService>();

        var now = DateTime.UtcNow;

        // Positive grant lots that have a past ExpiresAtUtc (uses IX_CreditTx_Expiry). We dedupe against
        // already-posted Expiry rows below via the (Type, RefId) idempotency key.
        var dueLots = await db.CreditTransactions
            .Where(t => t.ExpiresAtUtc != null && t.ExpiresAtUtc <= now && t.Amount > 0m)
            .OrderBy(t => t.ExpiresAtUtc)
            .Take(BatchSize)
            .Select(t => new { t.CreditTransactionId, t.UserId, t.Amount })
            .ToListAsync(ct);

        var expired = 0;
        foreach (var lot in dueLots)
        {
            var refId = $"CreditTx:{lot.CreditTransactionId}";

            // Skip lots we have already expired (fast pre-check; UX_CreditTx_TypeRef is the hard guard).
            var alreadyExpired = await db.CreditTransactions.AnyAsync(t =>
                t.Type == CreditTransactionType.Expiry && t.RefId == refId, ct);
            if (alreadyExpired) continue;

            // Clamp the deduction to the user's current balance so a partially/fully-spent lot does not
            // push the balance negative. Expire what is actually still there for this lot.
            var balance = (await credit.GetBalanceAsync(lot.UserId, ct)).Value?.Balance ?? 0m;
            var amountToExpire = Math.Min(lot.Amount, balance);

            if (amountToExpire <= 0m)
            {
                // Nothing left to expire (lot already spent). Still post a zero-effect marker? No — a zero
                // amount is rejected by ExpireAsync; instead post an idempotency marker is unnecessary because
                // there is no balance impact. We simply skip; a future grant cannot reuse this lot id.
                continue;
            }

            var result = await credit.ExpireAsync(lot.UserId, amountToExpire, refId, ct);
            if (result.Succeeded) expired++;
            else _logger.LogWarning("CreditExpiry: lot {Lot} expire failed: {Error}", lot.CreditTransactionId, result.Error);
        }

        if (dueLots.Count > 0)
            _logger.LogInformation("CreditExpiry: expired {Expired}/{Total} due credit lot(s).", expired, dueLots.Count);

        return expired;
    }
}
