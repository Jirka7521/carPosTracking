namespace CarPosAPI.Options;

/// <summary>
/// How this API is addressed from outside. Bound from the <c>Hosting</c> section;
/// the default is the production value, so the section normally does not need to
/// appear in appsettings at all.
///
/// <para>
/// The only setting here is <see cref="PathBase"/>, the URL prefix a reverse
/// proxy publishes the API under. The Cloudflare tunnel in front of this
/// deployment routes <c>jimajer.cz/carPosAPI/*</c> to the container and forwards
/// the prefix <em>as part of the path</em> — a tunnel has no way to strip it — so
/// without this the API would see <c>/carPosAPI/api/auth/login</c>, match no
/// route, and answer 404 to every request.
/// </para>
///
/// <para>
/// <see cref="Microsoft.AspNetCore.Builder.UsePathBaseExtensions.UsePathBase"/>
/// strips the prefix only when a request actually carries it, which is what lets
/// both spellings work at once: <c>/health</c> from inside the compose network
/// and <c>/carPosAPI/health</c> through the tunnel reach the same endpoint. It is
/// also why the prefix stays configuration rather than being baked into every
/// route attribute — the routes describe the API, not where it is published.
/// </para>
/// </summary>
public sealed class HostingOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "Hosting";

    /// <summary>
    /// URL prefix the API is published under, e.g. <c>/carPosAPI</c>. Absolute
    /// (leading slash) and without a trailing slash — ASP.NET Core joins it to
    /// the route itself, so a trailing slash would produce a path with <c>//</c>
    /// in generated <c>Location</c> headers.
    ///
    /// <para>
    /// Empty means the API is published at the root and nothing is stripped.
    /// Setting it does <em>not</em> stop the un-prefixed paths from working, so
    /// there is no cost to leaving it on for a deployment that is reached both
    /// ways.
    /// </para>
    /// </summary>
    public string PathBase { get; set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="PathBase"/> is a shape the pipeline can use. Checked at
    /// start-up so a typo fails the deployment immediately rather than showing up
    /// later as an unexplained 404 behind the proxy.
    /// </summary>
    /// <returns>True when the value is empty, or an absolute path with no trailing slash.</returns>
    public bool HasValidPathBase()
    {
        if (string.IsNullOrEmpty(PathBase))
        {
            return true;
        }

        return PathBase.StartsWith('/') && !PathBase.EndsWith('/');
    }
}
