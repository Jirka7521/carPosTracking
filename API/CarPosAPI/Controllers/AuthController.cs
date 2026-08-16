using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CarPosAPI.Controllers;

/// <summary>
/// Sign-up, sign-in and sign-out.
///
/// These are the only endpoints reachable without a session, which makes them the
/// API's front door and the one place worth attacking with volume. Two defences
/// live here: the rate-limiting policy applied to the whole controller, and the
/// service layer's refusal to distinguish "no such account" from "wrong password".
///
/// On success the session is written as cookies by
/// <see cref="ISessionCookieWriter"/> — the response body never contains the token.
/// </summary>
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Authentication)]
public sealed class AuthController : ApiControllerBase
{
    private readonly IUserAccountService _accounts;
    private readonly IJwtTokenIssuer _tokenIssuer;
    private readonly ISessionCookieWriter _cookieWriter;

    /// <summary>Creates the controller.</summary>
    /// <param name="accounts">Registration and credential checking.</param>
    /// <param name="tokenIssuer">Mints the session token.</param>
    /// <param name="cookieWriter">Writes and clears the session cookies.</param>
    public AuthController(
        IUserAccountService accounts,
        IJwtTokenIssuer tokenIssuer,
        ISessionCookieWriter cookieWriter)
    {
        _accounts = accounts;
        _tokenIssuer = tokenIssuer;
        _cookieWriter = cookieWriter;
    }

    /// <summary>Creates an account and signs the new user in immediately.</summary>
    /// <param name="request">Email, password and names.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the profile (and the session cookies), or 409 if the email is taken.</returns>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponseDto>> RegisterAsync(
        [FromBody] RegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        OperationResult<User> result = await _accounts.RegisterAsync(request, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        return SignIn(result.Value!);
    }

    /// <summary>Verifies credentials and starts a session.</summary>
    /// <param name="request">Email and password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the profile (and the session cookies), or 400 for bad credentials.</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> LoginAsync(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        OperationResult<User> result = await _accounts.AuthenticateAsync(request, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        return SignIn(result.Value!);
    }

    /// <summary>Ends the session by expiring its cookies.</summary>
    /// <returns>204, whether or not there was a session to end.</returns>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        // Deliberately unauthenticated and idempotent: logging out must work even
        // when the token has already expired, which is exactly when a user is most
        // likely to click the button.
        _cookieWriter.Clear(Response);
        return NoContent();
    }

    /// <summary>
    /// Issues a session for a user and returns their profile — the common tail of
    /// both register and login.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <returns>200 with the profile; the cookies ride along on the response.</returns>
    private ActionResult<AuthResponseDto> SignIn(User user)
    {
        IssuedToken token = _tokenIssuer.Issue(user);
        _cookieWriter.Issue(Response, token);

        return Ok(new AuthResponseDto(
            new UserProfileDto(user.Id, user.Email, user.FirstName, user.LastName)));
    }
}
