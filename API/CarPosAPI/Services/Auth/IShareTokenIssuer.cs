namespace CarPosAPI.Services.Auth;

/// <summary>
/// Signs the short-lived token an anonymous visitor holds after entering a share
/// link's code.
/// </summary>
public interface IShareTokenIssuer
{
    /// <summary>
    /// Issues a token for one share.
    /// </summary>
    /// <param name="shareId">The link the visitor opened.</param>
    /// <param name="validUntilUtc">
    /// End of the share's own window. The issued token never outlives it, so an
    /// expired share cannot be read with a token minted while it was alive.
    /// </param>
    /// <returns>The token and the lifetime its cookie must be given.</returns>
    IssuedToken Issue(Guid shareId, DateTime validUntilUtc);
}
