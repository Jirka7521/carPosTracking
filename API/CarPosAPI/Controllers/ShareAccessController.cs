using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Sharing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CarPosAPI.Controllers;

/// <summary>
/// The visitor's side of a share link: the only unauthenticated route to anybody's
/// position data in this API.
///
/// <para>
/// Two things make that safe rather than alarming. Opening a share needs both
/// halves of a link nobody can guess <em>and</em> a code sent by another channel,
/// with a growing cooldown between wrong answers and an IP rate limit outside
/// that. And reading one re-resolves the share from the database on every request,
/// so a window that has closed or a link that has been withdrawn stops working at
/// once rather than when a token lapses.
/// </para>
///
/// <para>
/// The read action names <see cref="ShareAuthenticationDefaults.Scheme"/>
/// explicitly. That is what keeps a session cookie from being accepted here and a
/// share cookie from being accepted anywhere else — the two schemes validate
/// different issuers and audiences, and neither can satisfy the other.
/// </para>
///
/// <para>
/// <b>There is no controller-level <c>[AllowAnonymous]</c>, and that is not a
/// stylistic choice.</b> One here would override the action-level
/// <c>[Authorize]</c> on <see cref="ViewAsync"/> — for anonymity the attribute
/// farther away wins — leaving the read action reachable with no share token
/// whatsoever. Each action declares its own access instead, which keeps the
/// analyser able to catch that mistake (ASP0026) rather than being silenced by it.
/// </para>
/// </summary>
[Route("api/shares")]
[EnableRateLimiting(RateLimitPolicies.ShareRedemption)]
public sealed class ShareAccessController : ApiControllerBase
{
    private readonly IShareRedemptionService _redemption;
    private readonly IShareViewService _view;
    private readonly IShareTokenIssuer _tokens;
    private readonly IShareCookieWriter _cookies;
    private readonly IShareContextAccessor _shareContext;

    /// <summary>Creates the controller.</summary>
    /// <param name="redemption">Checks the link and the code.</param>
    /// <param name="view">Reads the positions a share exposes.</param>
    /// <param name="tokens">Signs the share session token.</param>
    /// <param name="cookies">Delivers and clears that token.</param>
    /// <param name="shareContext">Supplies the share id on an authenticated share request.</param>
    public ShareAccessController(
        IShareRedemptionService redemption,
        IShareViewService view,
        IShareTokenIssuer tokens,
        IShareCookieWriter cookies,
        IShareContextAccessor shareContext)
    {
        _redemption = redemption;
        _view = view;
        _tokens = tokens;
        _cookies = cookies;
        _shareContext = shareContext;
    }

    /// <summary>
    /// Opens a share link with its code.
    ///
    /// The token arrives in the <b>body</b>, never the URL: the secret is already in
    /// the address of the page the visitor loaded, where the deployment's access log
    /// redacts it, and putting it in an API path would write it into a second log
    /// that has no such rule.
    /// </summary>
    /// <param name="request">The link secret and the code as typed.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the share's description, or a deliberately shaped refusal.</returns>
    [HttpPost("redeem")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ShareSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ShareSessionDto>> RedeemAsync(
        [FromBody] ShareRedeemRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<ShareRedemption> result =
            await _redemption.RedeemAsync(request.Token, request.Passphrase, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        ShareRedemption redemption = result.Value!;

        IssuedToken token = _tokens.Issue(redemption.ShareId, redemption.ValidUntil);
        _cookies.Issue(Response, token);

        return Ok(redemption.Session);
    }

    /// <summary>
    /// Returns the share's description and the fixes it currently exposes.
    /// </summary>
    /// <param name="from">Optional lower bound. Clamped to the window, and ignored for a latest-only share.</param>
    /// <param name="to">Optional upper bound. Clamped to the window, and ignored for a latest-only share.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the view, 401 without a valid share token, 404 once the share has ended.</returns>
    [HttpGet("view")]
    [Authorize(AuthenticationSchemes = ShareAuthenticationDefaults.Scheme)]
    [ProducesResponseType(typeof(SharedViewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SharedViewDto>> ViewAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        Guid shareId = RequireShareId(_shareContext);

        OperationResult<SharedViewDto> result = await _view.GetAsync(shareId, from, to, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Ends the visitor's share session by clearing the cookie.
    ///
    /// Anonymous rather than share-authenticated on purpose: "let go of whatever I
    /// am holding" must work even when what is held is expired or malformed, which
    /// is exactly when a visitor wants it to.
    /// </summary>
    /// <returns>204, always.</returns>
    [HttpPost("leave")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Leave()
    {
        _cookies.Clear(Response);

        return NoContent();
    }
}
