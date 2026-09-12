using System.Security.Claims;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Reads the share id out of the validated share token on the current request.
///
/// <para>
/// It looks for the claim on the identity issued by
/// <see cref="ShareAuthenticationDefaults.Scheme"/> specifically, not on whichever
/// identity happens to be present. A browser can legitimately hold both cookies at
/// once — somebody checking a link they created themselves — and in that case the
/// principal carries two identities. Picking the right one by name is what stops
/// the two sessions from bleeding into each other.
/// </para>
///
/// Scoped: <see cref="IHttpContextAccessor"/> is per-request state.
/// </summary>
internal sealed class ShareContextAccessor : IShareContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the accessor.</summary>
    /// <param name="httpContextAccessor">Supplies the ambient request.</param>
    public ShareContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Guid? ShareId
    {
        get
        {
            ClaimsPrincipal? principal = _httpContextAccessor.HttpContext?.User;

            if (principal is null)
            {
                return null;
            }

            // AuthenticationType is the scheme name for an identity the handler built,
            // so this is "the claim from the share token", not "a claim called share
            // from anywhere".
            ClaimsIdentity? shareIdentity = principal.Identities.FirstOrDefault(
                identity => identity.IsAuthenticated
                    && string.Equals(
                        identity.AuthenticationType,
                        ShareAuthenticationDefaults.Scheme,
                        StringComparison.Ordinal));

            string? value = shareIdentity?.FindFirst(ShareAuthenticationDefaults.ShareClaim)?.Value;

            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            // A token we signed always carries a well-formed Guid here. Parsing
            // defensively anyway keeps a malformed-but-validly-signed token from
            // throwing deep inside a service.
            bool parsed = Guid.TryParse(value, out Guid shareId);
            return parsed ? shareId : null;
        }
    }
}
