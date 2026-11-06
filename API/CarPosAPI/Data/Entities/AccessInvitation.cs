namespace CarPosAPI.Data.Entities;

/// <summary>
/// An open invitation to share a device with another account: a single-use link
/// whose recipient must sign in and accept it before any grant exists.
///
/// <para>
/// <b>Why invitations at all.</b> A grant used to be created on the spot for any
/// account the sharer named, without asking. Because a shared device is what lets
/// two accounts see each other's name and email, that let one side alone create the
/// relationship and read whoever it liked. An invitation is the other side's consent
/// made concrete: until it is accepted, nothing is shared and nobody is shown to
/// anybody. It is also not addressed to anyone — it is a link the sharer hands over
/// however they like — so making one reveals nothing about who has an account here.
/// </para>
///
/// <para>
/// The link uses the share-link token scheme (selector + verifier, the verifier kept
/// only as a digest), so a copy of this table opens no invitation. The row is deleted
/// when the invitation is accepted, declined or withdrawn; an expired one is simply
/// refused, and erased with its creator or its device.
/// </para>
/// Mapped by <see cref="Configurations.AccessInvitationConfiguration"/>.
/// </summary>
public sealed class AccessInvitation
{
    /// <summary>Primary key; the id the sharing list withdraws an invitation by.</summary>
    public Guid Id { get; set; }

    /// <summary>The public half of the token, used to find the row. Unique.</summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the secret half of the token. Never the verifier itself.</summary>
    public string VerifierHash { get; set; } = string.Empty;

    /// <summary>Internal row id of the device the invitation is for.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Navigation to the device.</summary>
    public Device? Device { get; set; }

    /// <summary>
    /// The account that made the invitation. Accepting re-checks that this account
    /// can still give what it offered, so an invitation never outlives its maker's
    /// right to share. Plain id, like <see cref="ShareLink.CreatedByUserId"/>: account
    /// erasure deletes the account's invitations itself.
    /// </summary>
    public int CreatedByUserId { get; set; }

    /// <summary>Capability the accepted grant receives.</summary>
    public bool CanDelete { get; set; }

    /// <summary>Capability the accepted grant receives.</summary>
    public bool CanShare { get; set; }

    /// <summary>Capability the accepted grant receives.</summary>
    public bool CanModifySettings { get; set; }

    /// <summary>When the invitation was made (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>After this instant (UTC) the invitation can no longer be accepted.</summary>
    public DateTime ExpiresAt { get; set; }
}
