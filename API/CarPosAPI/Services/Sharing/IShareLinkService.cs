using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Manages share links from their creator's side: minting, listing and revoking.
///
/// Every method authorises through the caller's own grant and requires
/// <c>CanShare</c> — the same capability that gates handing a device to another
/// account, because this hands it to someone with no account at all.
/// </summary>
public interface IShareLinkService
{
    /// <summary>Lists every link on a device, live and dead, newest first.</summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="deviceId">MQTT identity of the device.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The links, or the reason the caller may not see them.</returns>
    Task<OperationResult<IReadOnlyList<ShareLinkDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mints a link and its code. The returned secrets are not stored in
    /// recoverable form and cannot be produced again.
    /// </summary>
    /// <param name="userId">The authenticated caller, recorded as the creator.</param>
    /// <param name="request">Device, window, scope and telemetry choices.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The link plus its one-time secrets, or the reason it was refused.</returns>
    Task<OperationResult<ShareLinkCreatedDto>> CreateAsync(
        int userId,
        ShareLinkCreateRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Changes an existing link's window, label, scope and telemetry flags.
    ///
    /// <para>
    /// The link and its code are untouched, so whoever already holds them keeps
    /// working access — which is the point of editing rather than reissuing, and
    /// also why widening the window here is a real disclosure decision rather
    /// than a settings tweak.
    /// </para>
    ///
    /// <para>
    /// A <b>revoked</b> link is never editable. Expiry is time passing; revocation
    /// is a deliberate withdrawal, often because the link got somewhere it should
    /// not have. Letting an edit undo that would make "revoke" a promise this API
    /// does not keep.
    /// </para>
    /// </summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="shareId">The link to change.</param>
    /// <param name="request">The new settings — a full replacement.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The updated link, or the reason it was refused.</returns>
    Task<OperationResult<ShareLinkDto>> UpdateAsync(
        int userId,
        Guid shareId,
        ShareLinkUpdateRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mints a fresh link and code for an existing share, keeping its settings.
    ///
    /// <para>
    /// <b>This is the answer to "show me the link again", because there is no
    /// other one.</b> The verifier is stored as a SHA-256 digest and the code as a
    /// PBKDF2 hash, so the originals do not exist anywhere once the creation
    /// response is closed — not in the database, not in a log, not recoverable by
    /// anyone holding either. Re-displaying them is not a permission this API
    /// withholds; it is arithmetic it cannot do.
    /// </para>
    ///
    /// <para>
    /// The trade is explicit and the UI states it: <b>the previous link and code
    /// stop working immediately</b>, because the selector they resolve through is
    /// replaced. Anyone already using the share loses access until they are sent
    /// the new pair. The window, scope, telemetry flags, label and the redeem
    /// history all carry over — this reissues a share rather than replacing it.
    /// </para>
    /// </summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="shareId">The link to reissue.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The share with its new one-time secrets, or the reason it was refused.</returns>
    Task<OperationResult<ShareLinkCreatedDto>> ReissueAsync(
        int userId,
        Guid shareId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a link. Takes effect on the visitor's very next request, because
    /// the view path re-reads the row rather than trusting the token.
    /// </summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="shareId">The link to withdraw.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    Task<OperationResult<bool>> RevokeAsync(
        int userId,
        Guid shareId,
        CancellationToken cancellationToken);
}
