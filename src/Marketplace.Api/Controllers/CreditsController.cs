using Marketplace.Application.Credits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

/// <summary>
/// Platform credit endpoints (FR-29). Credit is NON-CASHABLE and NON-TRANSFERABLE.
/// IMPORTANT (Legal #1/#10): there is intentionally NO withdraw / cash-out / transfer endpoint here.
/// </summary>
[ApiController]
[Route("api/credits")]
[Authorize]
public class CreditsController : ApiControllerBase
{
    private readonly ICreditService _credit;

    public CreditsController(ICreditService credit) => _credit = credit;

    /// <summary>FR-29: current credit balance for the caller.</summary>
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);
        return FromResult(await _credit.GetBalanceAsync(userId, ct));
    }

    /// <summary>FR-29: transparent append-only ledger (newest first) with per-batch expiry dates.</summary>
    [HttpGet("ledger")]
    public async Task<IActionResult> GetLedger(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);
        return FromResult(await _credit.GetLedgerAsync(userId, ct));
    }

    // NOTE: no Withdraw/CashOut/Transfer action — credit cannot leave the platform (non-cashable).
}
