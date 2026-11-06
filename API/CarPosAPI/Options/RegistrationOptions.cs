using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Options;

/// <summary>
/// Who may sign up, bound from the <c>Registration</c> section. Policy rather than a
/// secret, like <see cref="DeviceRegistrationOptions"/>: kept in configuration so a
/// deployment can open or close sign-up without a rebuild.
///
/// <para>
/// <b>Why sign-up is not open by default.</b> This is a personal deployment holding
/// precise location histories, not a public service. Open sign-up let any stranger
/// on the internet get an account, and with it the authenticated surface every
/// other rule here has to hold against — including, at one point, a way to collect
/// every user's name and email. Invite-only keeps that surface to people the operator
/// chose. It also puts the invite check ahead of every other answer, so someone
/// without a code learns nothing from the form, not even whether an email address
/// already has an account.
/// </para>
/// </summary>
public sealed class RegistrationOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "Registration";

    /// <summary>Who may register; see <see cref="RegistrationMode"/>.</summary>
    public RegistrationMode Mode { get; set; } = RegistrationMode.InviteOnly;

    /// <summary>
    /// How long a newly minted invite code stays usable, in days, unless the CLI is
    /// given <c>--days</c>. Two weeks is long enough to reach somebody and short
    /// enough that a code forgotten in a chat history dies on its own.
    /// </summary>
    [Range(1, 365)]
    public int InviteLifetimeDays { get; set; } = 14;
}
