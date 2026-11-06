using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// The profile lookup the sharing list renders names with, plus self-service
/// profile and password edits.
///
/// The read endpoint is the only place one account can learn about another, so it
/// is deliberately narrow: it answers only for the caller themselves or somebody
/// they share a device with — and since grants now come only from an accepted
/// invitation, "share a device" means both people agreed to it. There used to be
/// an email search here as well, and a way to grant any account access by id;
/// between them they let any signed-in account enumerate every user's name and
/// email address. Sharing by invitation link needs neither, so both are gone.
///
/// The two mutating endpoints carry an id in the route because that is the shape
/// the frontend was built against — but they refuse any id that is not the
/// caller's own. There is no admin role, and nobody edits anybody else.
/// </summary>
[Route("api/users")]
[Authorize]
public sealed class UsersController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IUserAccountService _accounts;
    private readonly IJwtTokenIssuer _tokenIssuer;
    private readonly ISessionCookieWriter _cookieWriter;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="accounts">Does the account work.</param>
    /// <param name="tokenIssuer">Mints the replacement session after a password change.</param>
    /// <param name="cookieWriter">Writes it.</param>
    public UsersController(
        ICurrentUserAccessor currentUser,
        IUserAccountService accounts,
        IJwtTokenIssuer tokenIssuer,
        ISessionCookieWriter cookieWriter)
    {
        _currentUser = currentUser;
        _accounts = accounts;
        _tokenIssuer = tokenIssuer;
        _cookieWriter = cookieWriter;
    }

    /// <summary>Loads one user's profile, if the caller is entitled to see it.</summary>
    /// <param name="userId">The user to load.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the profile, or 404 — including for an account the caller may not see.</returns>
    [HttpGet("{userId:int}")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> GetAsync(int userId, CancellationToken cancellationToken)
    {
        // The sharing list is a set of ids that has to be rendered as names, which is
        // the whole reason this endpoint exists — so it answers for people the caller
        // already shares a device with, and for nobody else. It used to answer for
        // any id at all, which made a plain integer scan a user-table dump.
        OperationResult<UserProfileDto> result =
            await _accounts.GetVisibleProfileAsync(RequireUserId(_currentUser), userId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Updates the caller's own first and last name.</summary>
    /// <param name="userId">Must be the caller's own id.</param>
    /// <param name="request">The partial update.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the updated profile, or 403 for somebody else's id.</returns>
    [HttpPut("{userId:int}")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserProfileDto>> UpdateAsync(
        int userId,
        [FromBody] UserUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!IsSelf(userId))
        {
            return Forbid();
        }

        OperationResult<UserProfileDto> result =
            await _accounts.UpdateProfileAsync(userId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Changes the caller's own password.</summary>
    /// <param name="userId">Must be the caller's own id.</param>
    /// <param name="request">Current password (as proof) and the new one.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204 on success, 400 when the current password is wrong.</returns>
    [HttpPut("{userId:int}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangePasswordAsync(
        int userId,
        [FromBody] ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!IsSelf(userId))
        {
            return Forbid();
        }

        OperationResult<User> result =
            await _accounts.ChangePasswordAsync(userId, request, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        // The change ended every session under the old stamp, this one included;
        // the person who just proved they know the password keeps working.
        _cookieWriter.Issue(Response, _tokenIssuer.Issue(result.Value!));
        return NoContent();
    }

    /// <summary>Checks that a route id refers to the caller themselves.</summary>
    /// <param name="userId">The id from the route.</param>
    /// <returns>True when it is the caller's own id.</returns>
    private bool IsSelf(int userId)
    {
        return RequireUserId(_currentUser) == userId;
    }
}
