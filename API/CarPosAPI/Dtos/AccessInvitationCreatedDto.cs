namespace CarPosAPI.Dtos;

/// <summary>
/// Response of <c>POST /api/access/invitations</c>: the invitation, plus the one and
/// only copy of its link token. The dashboard builds the link
/// (<c>/invite/&lt;token&gt;</c>) and shows it once; the server keeps only a digest,
/// so a token that is lost is replaced by withdrawing the invitation and making a new one.
/// </summary>
/// <param name="Invitation">The invitation as it now appears in the sharing list.</param>
/// <param name="Token">The link token, <c>selector.verifier</c>. Never stored, never logged.</param>
public sealed record AccessInvitationCreatedDto(
    AccessInvitationDto Invitation,
    string Token);
