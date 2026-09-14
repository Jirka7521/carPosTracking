using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Turns a link plus a code into a share session, or refuses to.
///
/// This is the only unauthenticated way into anybody's position data, so it is the
/// one service in the application whose <em>failure</em> behaviour matters as much
/// as its success behaviour — see the implementation for what each refusal is
/// allowed to reveal.
/// </summary>
public interface IShareRedemptionService
{
    /// <summary>Attempts to open a share link.</summary>
    /// <param name="token">The <c>selector.verifier</c> secret from the URL.</param>
    /// <param name="passphrase">The code as the visitor typed it.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The opened share, or a deliberately shaped refusal.</returns>
    Task<OperationResult<ShareRedemption>> RedeemAsync(
        string token,
        string passphrase,
        CancellationToken cancellationToken);
}
