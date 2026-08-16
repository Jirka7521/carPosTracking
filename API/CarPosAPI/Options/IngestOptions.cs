using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Options;

/// <summary>
/// Hardening limits and retry behaviour for the MQTT ingest pipeline. Bound from
/// the optional <c>Ingest</c> configuration section; the defaults below are the
/// production values, sized from the firmware contract (max 40 envelopes of
/// ~0.9 KB per message), so the section normally does not need to appear in
/// appsettings at all. Every limit exists to bound the damage a compromised
/// device account could do: oversized messages, envelope floods and absurd
/// timestamps are all rejected before touching crypto or the database.
/// </summary>
public sealed class IngestOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "Ingest";

    /// <summary>
    /// Hard cap on the raw MQTT payload size. The realistic maximum is a 40-envelope
    /// backlog burst (~36 KB); 128 KB leaves generous headroom while stopping
    /// memory-exhaustion attempts cold.
    /// </summary>
    [Range(1024, 10 * 1024 * 1024)]
    public int MaxMessageBytes { get; set; } = 128 * 1024;

    /// <summary>
    /// Maximum envelopes per message. Firmware sends at most 40 per burst
    /// (<c>kSdMaxBurstFixes</c>); more than 64 means a misbehaving publisher and
    /// the whole message is dropped.
    /// </summary>
    [Range(1, 1024)]
    public int MaxEnvelopesPerMessage { get; set; } = 64;

    /// <summary>
    /// Maximum decoded ciphertext bytes per envelope. A real fix plaintext is
    /// ~150 bytes; 4 KB tolerates future firmware fields without allowing floods.
    /// </summary>
    [Range(64, 1024 * 1024)]
    public int MaxCiphertextBytes { get; set; } = 4096;

    /// <summary>
    /// Oldest acceptable GNSS fix time. The tracker did not exist before 2020, so
    /// anything earlier is a corrupt or forged timestamp. Backlogged fixes can be
    /// legitimately days old — that is why there is no tighter "recent" bound.
    /// </summary>
    public DateTime FixTimeMinUtc { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// How far into the future a fix time may point before it is rejected. Covers
    /// small GNSS/server clock disagreement without accepting forged times.
    /// </summary>
    [Range(0, 1440)]
    public int MaxFutureClockSkewMinutes { get; set; } = 10;

    /// <summary>Attempts for a failing database write before the message is redelivered.</summary>
    [Range(1, 10)]
    public int DbRetryCount { get; set; } = 3;

    /// <summary>Base delay of the exponential in-handler DB retry (2 s, 4 s, 8 s …).</summary>
    [Range(1, 300)]
    public int DbRetryBaseDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Pause before reconnecting after a database outage forced a disconnect.
    /// Long enough to avoid hammering a down database with redeliveries, short
    /// enough that ingestion resumes promptly once the outage ends.
    /// </summary>
    [Range(1, 3600)]
    public int DbFailureReconnectDelaySeconds { get; set; } = 15;

    /// <summary>
    /// How long an unknown/inactive device id is remembered as rejected before the
    /// database is asked again. Prevents an attacker publishing to made-up topics
    /// from turning every message into a database query.
    /// </summary>
    [Range(1, 1440)]
    public int UnknownDeviceNegativeCacheMinutes { get; set; } = 5;

    /// <summary>
    /// Lifetime of a cached device key entry before it is reloaded from the
    /// database. Bounds how long a deactivated device or rotated key keeps being
    /// honoured without an API restart.
    /// </summary>
    [Range(1, 1440)]
    public int DeviceCacheRefreshMinutes { get; set; } = 60;
}
