namespace CarPosAPI.Dtos;

/// <summary>
/// Response body of <c>POST /api/auth/register</c> and <c>POST /api/auth/login</c>.
///
/// SECURITY: the JWT is deliberately <em>not</em> in this body. It travels in an
/// <c>HttpOnly</c> cookie set on the same response, so the browser attaches it
/// automatically and no script — including one injected by an XSS bug — can ever
/// read it. The body carries only the profile the UI needs to render the header.
/// </summary>
/// <param name="User">The signed-in user's profile.</param>
public sealed record AuthResponseDto(UserProfileDto User);
