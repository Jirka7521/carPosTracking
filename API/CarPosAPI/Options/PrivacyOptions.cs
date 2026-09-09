using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Options;

/// <summary>
/// Who is answerable for the personal data this system holds, and which version of
/// the privacy policy is currently in force. Bound from the <c>Privacy</c>
/// configuration section.
///
/// This exists because a privacy policy is worthless without a named controller and
/// a working address to send a data-subject request to. <see cref="HasController"/>
/// is wired into the options pipeline in <c>Program.cs</c> so a Production
/// deployment with the placeholder still in place refuses to boot — the same
/// posture the JWT signing key takes, and for a comparable reason: shipping either
/// one unset is a defect that only shows up when it is too late to matter.
///
/// <see cref="PolicyVersion"/> is the string a new account accepts at registration
/// and that gets stamped on <see cref="Data.Entities.User"/>. It versions the terms
/// of use and the privacy policy together — one acceptance covers both, so one
/// version answers which text a given user agreed to. Bump it whenever either
/// changes materially.
///
/// Bumping it does not re-prompt anybody: there is no consent gate, and the accepted
/// version is not on the profile DTO. That is deliberate rather than missing. The
/// core processing runs on Art. 6(1)(b) contract, not consent, so no re-consent is
/// owed; the terms say material changes are announced and continued use is
/// acceptance. Do not describe this field as anything more than what it is: a record
/// of the text in force when each account was created.
/// </summary>
public sealed class PrivacyOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "Privacy";

    /// <summary>
    /// The value shipped in appsettings.json. Recognising it by name is what lets
    /// startup validation tell "nobody has filled this in" apart from a real
    /// address, without hard-coding the check into a string comparison elsewhere.
    /// </summary>
    public const string UnsetContactPlaceholder = "SET-CONTROLLER-CONTACT-EMAIL";

    /// <summary>
    /// The natural or legal person answerable for the processing, as it appears in
    /// the privacy policy and the Art. 30 record.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string ControllerName { get; set; } = "Jiri Majer";

    /// <summary>
    /// Where a data subject sends an access, rectification or erasure request that
    /// the in-app controls do not cover. Must be a real, monitored address before
    /// anyone but the author uses the deployment.
    /// </summary>
    [Required]
    [StringLength(256, MinimumLength = 3)]
    public string ControllerContactEmail { get; set; } = UnsetContactPlaceholder;

    /// <summary>
    /// Version of the terms of use and privacy policy currently in force — a date
    /// string. The text itself lives in FE/src/i18n/locales/{en,cs}/legal.json, which
    /// is the only copy; docs/PRIVACY.md is a pointer, not a second source. Recorded
    /// against every account that accepts it, so it is always answerable which text a
    /// given user agreed to.
    /// </summary>
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string PolicyVersion { get; set; } = "2026-09-09";

    /// <summary>
    /// True when a real contact address has been configured. False while the
    /// shipped placeholder is still in place, or when the value is obviously not an
    /// address.
    /// </summary>
    /// <returns><c>true</c> when the controller contact is usable.</returns>
    public bool HasController()
    {
        if (string.IsNullOrWhiteSpace(ControllerContactEmail))
        {
            return false;
        }

        if (string.Equals(ControllerContactEmail, UnsetContactPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Not an email validator — just enough to catch a value nobody could write
        // to. Anything more elaborate would reject addresses that are perfectly
        // legal and would still not prove the mailbox is read.
        return ControllerContactEmail.Contains('@', StringComparison.Ordinal);
    }
}
