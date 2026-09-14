namespace CarPosAPI.Services.Auth;

/// <summary>
/// Writes and clears the cookie carrying an anonymous visitor's share token.
/// </summary>
public interface IShareCookieWriter
{
    /// <summary>Writes the share cookie with a Max-Age matching the token's life.</summary>
    /// <param name="response">The response being sent to the visitor.</param>
    /// <param name="token">The freshly issued share token and its lifetime.</param>
    void Issue(HttpResponse response, IssuedToken token);

    /// <summary>Expires the share cookie, ending the visitor's session immediately.</summary>
    /// <param name="response">The response being sent to the visitor.</param>
    void Clear(HttpResponse response);
}
