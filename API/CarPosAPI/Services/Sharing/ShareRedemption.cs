using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// A successful redemption: which share was opened, when its window closes, and
/// what the visitor may be told about it.
///
/// <para>
/// <see cref="ValidUntil"/> is carried separately from the session description
/// because it is not a display value — the token issuer uses it to make sure a
/// share session cannot be minted with a life longer than the share itself.
/// </para>
/// </summary>
/// <param name="ShareId">The link's id, which becomes the token's <c>share</c> claim.</param>
/// <param name="ValidUntil">End of the window (UTC), the ceiling on the issued token's life.</param>
/// <param name="Session">What the visitor is told about the share.</param>
public sealed record ShareRedemption(
    Guid ShareId,
    DateTime ValidUntil,
    ShareSessionDto Session);
