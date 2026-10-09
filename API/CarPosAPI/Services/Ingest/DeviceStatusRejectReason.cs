namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Why a decrypted status payload was rejected by <see cref="DeviceStatusValidator"/>.
/// Only the things that make a message meaningless reject it — a bad optional field is
/// dropped to null instead, because an event with no battery figure is still worth far
/// more than no event.
/// </summary>
internal enum DeviceStatusRejectReason
{
    /// <summary>Not rejected.</summary>
    None = 0,

    /// <summary>The decrypted bytes are not the expected JSON object.</summary>
    JsonInvalid,

    /// <summary><c>device</c> or <c>type</c> is missing, or an offline message has no <c>reason</c>.</summary>
    MissingField,

    /// <summary>
    /// The device id inside the encrypted payload differs from the topic — the same
    /// cross-device integrity check positions get.
    /// </summary>
    DeviceMismatch,

    /// <summary><c>type</c> is neither <c>online</c> nor <c>offline</c>.</summary>
    UnknownType,

    /// <summary>An offline <c>reason</c> the firmware is not known to send.</summary>
    UnknownReason,
}
