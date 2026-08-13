namespace CarPosAPI.Services.Auth;

/// <summary>
/// A freshly minted session token and how long it stays valid. The lifetime
/// travels alongside the string because the cookie that carries the token must be
/// given the same one — a cookie that outlives its token produces requests that
/// look authenticated to the browser and 401 at the server.
///
/// It is a <em>duration</em> rather than an absolute expiry on purpose. The
/// browser is the one that decides when this cookie dies, and it judges that by
/// its own clock; handing it a moment computed from ours makes the session
/// hostage to the two clocks agreeing. See
/// <see cref="SessionCookieWriter.Issue"/> for what that failure looks like.
/// </summary>
/// <param name="Token">The compact-serialised JWT. Secret: never logged.</param>
/// <param name="Lifetime">How long the token remains valid from the moment it was issued.</param>
public sealed record IssuedToken(string Token, TimeSpan Lifetime);
