namespace CarPosAPI.Services.Common;

/// <summary>
/// The stable, machine-readable name of every failure this API reports, sent as
/// the <c>code</c> extension of each ProblemDetails response.
///
/// <para>
/// <b>Why codes exist next to the English <c>detail</c>:</b> the frontend is
/// translated, and the only thing it could key a translation on before was the
/// English sentence itself — so rewording a message on the server silently broke
/// the Czech one. A code is a contract instead: the sentence may change freely,
/// the code may not. The frontend looks each one up as <c>errors:api.&lt;code&gt;</c>
/// (FE/src/i18n/locales/*/errors.json), so <b>a new code needs a translation
/// there in the same change</b>, and renaming one is a breaking change for it.
/// </para>
///
/// <para>
/// Codes carry exactly the information the <c>detail</c> sentence already carried
/// and nothing more — the "404, not 403" enumeration rule applies to them in the
/// same way it applies to the text.
/// </para>
///
/// <para>
/// Values a message needs (a profile name, a count) travel separately in the
/// <c>params</c> extension, see <see cref="ServiceError.Parameters"/>. A parameter
/// named <c>count</c> is what the frontend uses to choose a plural form.
/// </para>
/// </summary>
public static class ErrorCodes
{
    // ----- Accounts -----

    /// <summary>Login with an unknown email or a wrong password (deliberately one code).</summary>
    public const string InvalidCredentials = "invalidCredentials";

    /// <summary>Registration with an email that already has an account.</summary>
    public const string EmailTaken = "emailTaken";

    /// <summary>The policy version the registrant accepted is no longer current.</summary>
    public const string PrivacyPolicyChanged = "privacyPolicyChanged";

    /// <summary>The user does not exist, or is not visible to the caller.</summary>
    public const string NoSuchUser = "noSuchUser";

    /// <summary>Password change with a wrong current password.</summary>
    public const string WrongCurrentPassword = "wrongCurrentPassword";

    /// <summary>Account erasure confirmed with a wrong password.</summary>
    public const string WrongPassword = "wrongPassword";

    // ----- Devices and provisioning -----

    /// <summary>The device does not exist, or the caller has no grant on it.</summary>
    public const string NoSuchDevice = "noSuchDevice";

    /// <summary>The device id is already registered. Params: <c>deviceId</c>.</summary>
    public const string DeviceIdTaken = "deviceIdTaken";

    /// <summary>Device registration without the tracking declaration.</summary>
    public const string TrackingDeclarationRequired = "trackingDeclarationRequired";

    /// <summary>The caller lacks the delete capability on the device.</summary>
    public const string NoPermissionDeleteDevice = "noPermissionDeleteDevice";

    /// <summary>The caller may not erase the device's position history.</summary>
    public const string NoPermissionDeleteData = "noPermissionDeleteData";

    /// <summary>The caller may not view the firmware configuration.</summary>
    public const string NoPermissionViewFirmware = "noPermissionViewFirmware";

    /// <summary>The caller may not change the firmware configuration.</summary>
    public const string NoPermissionChangeFirmware = "noPermissionChangeFirmware";

    /// <summary>The firmware configuration cannot be rendered without a public key.</summary>
    public const string NoStoredPublicKey = "noStoredPublicKey";

    /// <summary>An ack key import with an empty key.</summary>
    public const string AckKeyMissing = "ackKeyMissing";

    /// <summary>An ack key import that supplied a private key.</summary>
    public const string AckKeyIsPrivate = "ackKeyIsPrivate";

    /// <summary>An ack key import that is not a PEM public key.</summary>
    public const string AckKeyInvalid = "ackKeyInvalid";

    /// <summary>An ack key of the wrong size. Params: <c>bits</c>, <c>expected</c>.</summary>
    public const string AckKeyWrongSize = "ackKeyWrongSize";

    // ----- Device settings -----

    /// <summary>The caller may not view the device's settings.</summary>
    public const string NoPermissionViewSettings = "noPermissionViewSettings";

    /// <summary>The caller may not change the device's settings.</summary>
    public const string NoPermissionChangeSettings = "noPermissionChangeSettings";

    /// <summary>Settings change on a soft-deleted device.</summary>
    public const string SettingsDeviceDeleted = "settingsDeviceDeleted";

    /// <summary>A manual save on a scheduled device, without the acknowledgement.</summary>
    public const string ScheduleOverrideNeedsConfirm = "scheduleOverrideNeedsConfirm";

    /// <summary>A temporary override on a schedule that never switches.</summary>
    public const string ScheduleNeverSwitches = "scheduleNeverSwitches";

    /// <summary>Republish with nothing stored to publish.</summary>
    public const string NoStoredConfigToPublish = "noStoredConfigToPublish";

    /// <summary>The device has no stored configuration.</summary>
    public const string NoStoredConfig = "noStoredConfig";

    /// <summary>Settings saved, but the MQTT broker could not be reached (503).</summary>
    public const string BrokerUnavailable = "brokerUnavailable";

    // ----- Schedule -----

    /// <summary>The caller may not use the device's schedule.</summary>
    public const string NoPermissionSchedule = "noPermissionSchedule";

    /// <summary>Schedule change on a soft-deleted device.</summary>
    public const string ScheduleDeviceDeleted = "scheduleDeviceDeleted";

    /// <summary>Resume requested with no schedule configured.</summary>
    public const string NoScheduleToResume = "noScheduleToResume";

    /// <summary>The profile does not exist on this device.</summary>
    public const string NoSuchProfile = "noSuchProfile";

    /// <summary>The rule does not exist on this device.</summary>
    public const string NoSuchRule = "noSuchRule";

    /// <summary>A rule names a profile of another device.</summary>
    public const string ProfileNotOwned = "profileNotOwned";

    /// <summary>The fallback names a profile of another device.</summary>
    public const string FallbackProfileNotOwned = "fallbackProfileNotOwned";

    /// <summary>Enabling a schedule without a fallback profile.</summary>
    public const string FallbackProfileRequired = "fallbackProfileRequired";

    /// <summary>The profile limit is reached. Params: <c>max</c>.</summary>
    public const string TooManyProfiles = "tooManyProfiles";

    /// <summary>The rule limit is reached. Params: <c>max</c>.</summary>
    public const string TooManyRules = "tooManyRules";

    /// <summary>A profile name that is already taken. Params: <c>name</c>.</summary>
    public const string DuplicateProfileName = "duplicateProfileName";

    /// <summary>Deleting a profile rules still use. Params: <c>name</c>, <c>count</c>.</summary>
    public const string ProfileInUse = "profileInUse";

    /// <summary>Deleting the schedule's fallback profile. Params: <c>name</c>.</summary>
    public const string ProfileIsFallback = "profileIsFallback";

    // ----- Sharing with accounts -----

    /// <summary>The caller may not share the device.</summary>
    public const string NoPermissionShare = "noPermissionShare";

    /// <summary>The caller may not manage the device's sharing.</summary>
    public const string NoPermissionManageSharing = "noPermissionManageSharing";

    /// <summary>The user already has a grant on the device.</summary>
    public const string AccessExists = "accessExists";

    /// <summary>Removing the last grant that can share the device.</summary>
    public const string LastSharer = "lastSharer";

    /// <summary>The access grant does not exist, or is not the caller's to manage.</summary>
    public const string NoSuchAccessGrant = "noSuchAccessGrant";

    // ----- Share links -----

    /// <summary>The share link does not exist, or is not the caller's to manage.</summary>
    public const string NoSuchShareLink = "noSuchShareLink";

    /// <summary>A share link created without a scope.</summary>
    public const string ShareScopeRequired = "shareScopeRequired";

    /// <summary>The device's active share link limit is reached.</summary>
    public const string TooManyShareLinks = "tooManyShareLinks";

    /// <summary>Editing a revoked link.</summary>
    public const string LinkRevokedImmutable = "linkRevokedImmutable";

    /// <summary>Re-issuing a revoked link.</summary>
    public const string LinkRevoked = "linkRevoked";

    /// <summary>A sharing window that ends before it starts.</summary>
    public const string WindowEndBeforeStart = "windowEndBeforeStart";

    /// <summary>A sharing window entirely in the past.</summary>
    public const string WindowPassed = "windowPassed";

    /// <summary>A sharing window longer than allowed.</summary>
    public const string WindowTooLong = "windowTooLong";

    /// <summary>A visitor opened a link its creator revoked.</summary>
    public const string LinkWithdrawn = "linkWithdrawn";

    /// <summary>A visitor opened a link after its window.</summary>
    public const string LinkExpired = "linkExpired";

    /// <summary>A visitor opened a link before its window.</summary>
    public const string LinkNotActiveYet = "linkNotActiveYet";

    /// <summary>A visitor typed a wrong share code.</summary>
    public const string WrongShareCode = "wrongShareCode";

    /// <summary>Too many wrong share codes. Params: <c>count</c> (minutes left).</summary>
    public const string ShareCodeCooldown = "shareCodeCooldown";

    /// <summary>A share address that does not resolve to any link.</summary>
    public const string LinkInvalid = "linkInvalid";

    /// <summary>A share session whose link has gone away.</summary>
    public const string LinkUnavailable = "linkUnavailable";

    // ----- Request-level (middleware and framework responses) -----

    /// <summary>A mutating request without a valid CSRF token.</summary>
    public const string CsrfInvalid = "csrfInvalid";

    /// <summary>The automatic DataAnnotations 400. Params: <c>fields</c>.</summary>
    public const string ValidationFailed = "validationFailed";

    /// <summary>A request Kestrel could not read.</summary>
    public const string RequestUnreadable = "requestUnreadable";

    /// <summary>A request body over the size limit.</summary>
    public const string BodyTooLarge = "bodyTooLarge";

    /// <summary>Request headers over the size limit.</summary>
    public const string HeadersTooLarge = "headersTooLarge";

    /// <summary>No valid session (bare 401).</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>A bare 403 from authorization.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>A bare 404, e.g. an unmatched route.</summary>
    public const string NotFound = "notFound";

    /// <summary>A bare 405.</summary>
    public const string MethodNotAllowed = "methodNotAllowed";

    /// <summary>A 429 from the rate limiter.</summary>
    public const string TooManyRequests = "tooManyRequests";

    /// <summary>An unhandled exception (500) or any other server-side failure.</summary>
    public const string ServerError = "serverError";
}
