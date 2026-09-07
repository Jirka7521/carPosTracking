namespace CarPosAPI.Dtos;

/// <summary>
/// Response of <c>GET /api/privacy/policy</c>. Public, because the registration
/// form has to render the acknowledgement before anyone has an account.
///
/// The version is the point of it: the form echoes back whatever version it
/// displayed, and registration refuses anything but the current one, so the
/// acceptance recorded against an account is provably the text that account holder
/// was shown.
/// </summary>
/// <param name="Version">The privacy-policy version currently in force.</param>
/// <param name="ControllerName">Who is answerable for the processing.</param>
/// <param name="ControllerContactEmail">Where to send a data-subject request.</param>
public sealed record PrivacyPolicyDto(
    string Version,
    string ControllerName,
    string ControllerContactEmail);
