using System.Security.Claims;
using Marketplace.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

/// <summary>
/// Shared helpers for the API controllers: pulls the authenticated user id out of the JWT and maps the
/// service-layer <see cref="Result"/>/<see cref="Result{T}"/> into HTTP responses + RFC 7807 ProblemDetails.
/// Result.Error is a short message from the service; we classify it into 400/403/404 by simple convention
/// (services return human messages, not codes) so the API stays consistent without leaking internals.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The authenticated caller's UserId (from the <c>sub</c>/NameIdentifier claim), or null if unauthenticated.</summary>
    protected Guid? CurrentUserId
    {
        get
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    /// <summary>True when the caller carries the Admin role claim.</summary>
    protected bool IsAdmin => User.IsInRole("Admin");

    /// <summary>Map a value-result: 200 with the value on success, else a ProblemDetails with a classified status.</summary>
    protected IActionResult FromResult<T>(Result<T> result, int successStatus = StatusCodes.Status200OK)
        => result.Succeeded
            ? StatusCode(successStatus, result.Value)
            : Problem(result.Error);

    /// <summary>Map a void-result: 204 No Content on success, else a classified ProblemDetails.</summary>
    protected IActionResult FromResult(Result result)
        => result.Succeeded ? NoContent() : Problem(result.Error);

    /// <summary>Classify a service error message into 404 / 403 / 400 and wrap it as ProblemDetails.</summary>
    protected IActionResult Problem(string? error)
    {
        var message = string.IsNullOrWhiteSpace(error) ? "Request could not be processed." : error;
        var status = ClassifyStatus(message);
        return Problem(detail: message, statusCode: status);
    }

    private static int ClassifyStatus(string message)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("not found"))
            return StatusCodes.Status404NotFound;
        if (m.Contains("not allowed") || m.Contains("forbidden") || m.Contains("cannot")
            || m.Contains("required to") || m.Contains("not active"))
            return StatusCodes.Status403Forbidden;
        return StatusCodes.Status400BadRequest;
    }
}
