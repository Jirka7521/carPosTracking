namespace CarPosAPI.Services.Auth;

/// <summary>
/// Puts a session onto a response, and takes it off again. Isolating this means
/// the cookie flags that make the scheme safe — <c>HttpOnly</c>, <c>Secure</c>,
/// <c>SameSite</c> — are decided in exactly one place rather than being retyped
/// (and eventually mistyped) at every sign-in path.
/// </summary>
public interface ISessionCookieWriter
{
    /// <summary>Writes the session and CSRF cookies onto a response.</summary>
    /// <param name="response">The response being sent to the browser.</param>
    /// <param name="token">The freshly issued session token and its expiry.</param>
    void Issue(HttpResponse response, IssuedToken token);

    /// <summary>Expires both cookies, ending the session client-side.</summary>
    /// <param name="response">The response being sent to the browser.</param>
    void Clear(HttpResponse response);
}
