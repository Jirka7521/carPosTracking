namespace CarPosAPI.Dtos;

/// <summary>
/// How many parties can see one device right now — the dashboard's answer to
/// "who is watching this vehicle?", reduced to two numbers.
///
/// <para>
/// <b>Counts, never identities.</b> The device grid shows this to everybody who
/// can see the device, including people holding nothing but <c>CanRead</c>, so it
/// deliberately carries no names, addresses or labels. Who those people are is a
/// separate, permission-gated question answered by <c>GET /api/access</c> and
/// <c>GET /api/shares</c>, both of which require <c>CanShare</c>. Keeping the
/// aggregate free of identities is what lets it be shown that widely: learning
/// that three people can see a car you can also see discloses nothing about them.
/// </para>
///
/// <para>
/// The two numbers are kept apart rather than summed because they are different
/// kinds of reach. An <see cref="Data.Entities.Access"/> row is an account with a
/// standing grant; a <see cref="Data.Entities.ShareLink"/> is an anonymous window
/// that closes by itself. A single total would hide that distinction at exactly
/// the moment somebody is trying to understand their own exposure.
/// </para>
/// </summary>
/// <param name="People">
/// Accounts holding an active grant on the device — <b>one per account</b>,
/// whatever capabilities it carries, and including the caller themselves. Always
/// at least 1: a caller who could not see the device would not have received this
/// record at all. Revoked (inactive) grants are not counted.
/// </param>
/// <param name="ActiveLinks">
/// Share links that are <b>live at this instant</b>: not revoked, and with the
/// current time inside their window. <b>One per link, regardless of how many
/// times it has been opened</b> — the link is the unit of access, and its
/// <see cref="Data.Entities.ShareLink.SuccessfulRedeems"/> counter says nothing
/// about how many people hold it.
///
/// Revoked, expired and not-yet-started links are excluded, which is what makes
/// this a "currently" figure rather than a history. The rule mirrors
/// <see cref="Services.Sharing.ShareLinkStatusResolver"/>: counted here are
/// exactly the links it would call <c>active</c> or <c>coolingDown</c>. A
/// cooling-down link counts because the cooldown is a temporary refusal of a
/// wrong code, not a withdrawal of access — the holder still has it.
/// </param>
public sealed record DeviceAccessCountsDto(int People, int ActiveLinks);
