using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Reads the caller's id out of the validated JWT on the current request.
///
/// It reads the raw <c>sub</c> claim because <c>MapInboundClaims</c> is turned off
/// in <c>Program.cs</c> — that mapping silently renames <c>sub</c> to the long
/// <c>nameidentifier</c> URI, and code that looks for one while the framework
/// produced the other fails as "logged in but has no id", which is a confusing way
/// to spend an afternoon.
///
/// Scoped: <see cref="IHttpContextAccessor"/> is per-request state.
/// </summary>
internal sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the accessor.</summary>
    /// <param name="httpContextAccessor">Supplies the ambient request.</param>
    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public int? UserId
    {
        get
        {
            ClaimsPrincipal? principal = _httpContextAccessor.HttpContext?.User;
            if (principal is null || principal.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            string? subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrEmpty(subject))
            {
                return null;
            }

            // A token we signed always carries an integer here. Parsing defensively
            // anyway costs nothing and keeps a malformed-but-validly-signed token
            // (say, after a future claim change) from throwing deep inside a service.
            bool parsed = int.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out int userId);
            return parsed ? userId : null;
        }
    }
}
