namespace CarPosAPI.Services.Ingest;

/// <summary>
/// How the MQTT service must acknowledge a processed message. There are only two
/// verbs in MQTT 3.1.1 — ack or don't — so every failure has to be classified
/// into one of them.
/// </summary>
internal enum IngestOutcome
{
    /// <summary>
    /// Acknowledge the message. Includes poison messages (bad crypto, bad data):
    /// redelivering those would loop forever and stall the broker's in-flight
    /// window, so they are logged, counted and consumed.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Do not acknowledge: the message content is (probably) fine but the
    /// database could not take it. The service disconnects and reconnects after a
    /// pause so the broker redelivers — nothing is lost.
    /// </summary>
    RetryableFailure,
}
