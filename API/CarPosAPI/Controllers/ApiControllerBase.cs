using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// Shared base for every controller in this API. It owns the two pieces of
/// plumbing that would otherwise be copy-pasted into each action: translating a
/// service's <see cref="OperationOutcome"/> into an HTTP status code, and getting
/// the caller's id.
///
/// Keeping the translation in one place is what makes the status codes
/// consistent — "a permission failure is 403, a missing row is 404, a duplicate is
/// 409" is enforced by this class rather than by everyone remembering it.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The authenticated caller's id.
    ///
    /// Every action that reads this sits behind <c>[Authorize]</c>, so the token
    /// has already been validated and the claim is present. If it somehow is not,
    /// throwing is the right answer: continuing would mean running a query with an
    /// unknown user id, and that is how data leaks between accounts.
    /// </summary>
    /// <param name="accessor">The current-user accessor from DI.</param>
    /// <returns>The caller's user id.</returns>
    /// <exception cref="InvalidOperationException">The request is not authenticated.</exception>
    protected static int RequireUserId(ICurrentUserAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        return accessor.UserId
            ?? throw new InvalidOperationException(
                "An authorized endpoint was reached without a user id claim. Check the [Authorize] attribute and the JWT configuration.");
    }

    /// <summary>
    /// Turns a failed service result into the matching <see cref="ProblemDetails"/>
    /// response.
    /// </summary>
    /// <typeparam name="TValue">The value type the result would have carried.</typeparam>
    /// <param name="result">A result whose outcome is <em>not</em> success.</param>
    /// <returns>The problem response for that outcome.</returns>
    /// <exception cref="ArgumentException">The result actually succeeded.</exception>
    protected ObjectResult Failure<TValue>(OperationResult<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            throw new ArgumentException("Failure() was called with a successful result.", nameof(result));
        }

        (int statusCode, string title) = result.Outcome switch
        {
            OperationOutcome.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            OperationOutcome.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            OperationOutcome.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status400BadRequest, "Invalid request"),
        };

        // result.Detail is written for the end user by the service layer, so it is
        // safe to surface verbatim — no exception messages, SQL or stack traces
        // ever reach it.
        return Problem(title: title, detail: result.Detail, statusCode: statusCode);
    }
}
