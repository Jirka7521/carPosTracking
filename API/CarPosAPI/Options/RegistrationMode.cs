namespace CarPosAPI.Options;

/// <summary>
/// Who may create an account through <c>POST /api/auth/register</c>. Bound by name
/// from <c>Registration:Mode</c> (see <see cref="RegistrationOptions"/>).
/// </summary>
public enum RegistrationMode
{
    /// <summary>
    /// Only someone holding an unused, unexpired invite code. Codes are minted by the
    /// operator with the <c>create-registration-invite</c> CLI mode. The default.
    /// </summary>
    InviteOnly = 0,

    /// <summary>Anyone on the internet may sign up. Not recommended for this deployment.</summary>
    Open = 1,

    /// <summary>Nobody may sign up; existing accounts keep working.</summary>
    Closed = 2,
}
