namespace CarPosAPI.Services.Privacy;

/// <summary>
/// One temporary share link as it appears in a data export.
///
/// <para>
/// <b>Note what the projection has no field for:</b> <c>Selector</c>,
/// <c>VerifierHash</c> and <c>PassphraseHash</c>. That is the same defence
/// <see cref="UserExportRow"/> uses against the password hash and
/// <see cref="DeviceExportRow"/> against the device private key — a secret with no
/// property to travel in cannot be exported however this document is later
/// reshaped, and <c>DataExportShapeTests</c> fails the build if one appears.
/// </para>
///
/// <para>
/// The link itself is not exported and could not be: the verifier survives only as
/// a digest. That is the correct answer to Art. 20 as well as a security property
/// — what the data subject is entitled to port is the record that they shared
/// something, with whom it was shared being a thing this system deliberately never
/// learns.
/// </para>
/// </summary>
/// <param name="DeviceId">MQTT identity of the device the link exposes.</param>
/// <param name="Label">What the link's visitor sees the tracker called.</param>
/// <param name="ValidFrom">Start of the window (UTC).</param>
/// <param name="ValidUntil">End of the window (UTC).</param>
/// <param name="Scope">Whether the link shows the latest fix only or the whole track.</param>
/// <param name="IncludeSpeed">Whether speed is disclosed with each fix.</param>
/// <param name="IncludeTelemetry">Whether battery and temperature are disclosed with each fix.</param>
/// <param name="CreatedAt">When the link was minted (UTC).</param>
/// <param name="RevokedAt">When it was withdrawn (UTC), or null.</param>
/// <param name="SuccessfulRedeems">How many times it was opened successfully.</param>
/// <param name="LastAccessedAt">When it was last opened successfully (UTC), or null.</param>
public sealed record ShareLinkExportRow(
    string DeviceId,
    string Label,
    DateTime ValidFrom,
    DateTime ValidUntil,
    string Scope,
    bool IncludeSpeed,
    bool IncludeTelemetry,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    int SuccessfulRedeems,
    DateTime? LastAccessedAt);
