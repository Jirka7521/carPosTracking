using System.ComponentModel.DataAnnotations;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// User lookups for the sharing UI, plus self-service profile and password edits.
///
/// The search endpoint is the only place one account can learn about another, so
/// it is deliberately narrow: it matches on email, caps its results, and refuses
/// prefixes short enough to be used as a directory dump (see
/// <see cref="IUserAccountService.SearchByEmailAsync"/>). It returns nothing an
/// account holder would not print on a business card.
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

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="accounts">Does the account work.</param>
    public UsersController(ICurrentUserAccessor currentUser, IUserAccountService accounts)
    {
        _currentUser = currentUser;
        _accounts = accounts;
    }

    /// <summary>Finds users by email address, for the "share with…" picker.</summary>
    /// <param name="email">The address to look for.</param>
    /// <param name="exactMatch">True (the default) for equality; false for a capped prefix search.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the matches — an empty list when there are none.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserProfileDto>>> SearchAsync(
        [FromQuery][Required][StringLength(256, MinimumLength = 1)] string email,
        [FromQuery] bool exactMatch,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<UserProfileDto> matches =
            await _accounts.SearchByEmailAsync(email, exactMatch, cancellationToken);

        // "No such user" is 200-with-empty-list, not 404: the caller asked a
        // question ("who matches?") and got a complete answer.
        return Ok(matches);
    }

    /// <summary>Loads one user's profile.</summary>
    /// <param name="userId">The user to load.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the profile, or 404.</returns>
    [HttpGet("{userId:int}")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> GetAsync(int userId, CancellationToken cancellationToken)
    {
        // Any signed-in user may resolve any id, because the sharing list is a set
        // of ids that has to be rendered as names. The profile carries nothing
        // sensitive — see UserProfileDto.
        OperationResult<UserProfileDto> result = await _accounts.GetProfileAsync(userId, cancellationToken);

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

        OperationResult<bool> result =
            await _accounts.ChangePasswordAsync(userId, request, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }

    /// <summary>Checks that a route id refers to the caller themselves.</summary>
    /// <param name="userId">The id from the route.</param>
    /// <returns>True when it is the caller's own id.</returns>
    private bool IsSelf(int userId)
    {
        return RequireUserId(_currentUser) == userId;
    }
}
