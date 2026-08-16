using CarPosAPI.Data.Entities;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Mints the session JWT. Validation of that token is the framework's job
/// (configured in <c>Program.cs</c>); this interface owns only the issuing side,
/// so there is exactly one place that decides what a session claims to be.
/// </summary>
public interface IJwtTokenIssuer
{
    /// <summary>Issues a session token for a user.</summary>
    /// <param name="user">The authenticated user.</param>
    /// <returns>The signed token and its absolute expiry.</returns>
    IssuedToken Issue(User user);
}
