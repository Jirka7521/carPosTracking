namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Outcome of decoding one MQTT message with <see cref="EnvelopeCodec"/>.
/// A fatal error (unparseable array, size caps exceeded) voids the whole
/// message; individually malformed envelopes are merely counted so one bad
/// element never sinks the valid fixes travelling beside it in a backlog burst.
/// </summary>
/// <param name="Envelopes">Structurally valid envelopes, in message order.</param>
/// <param name="RejectedEnvelopes">Count of envelopes dropped by structural checks.</param>
/// <param name="FatalError">Reason the entire message is unusable, or null.</param>
internal sealed record EnvelopeDecodeResult(
    IReadOnlyList<DecodedEnvelope> Envelopes,
    int RejectedEnvelopes,
    string? FatalError)
{
    /// <summary>Creates a fatal result carrying no envelopes.</summary>
    /// <param name="error">Human-readable (log-safe) reason.</param>
    /// <returns>A result whose <see cref="FatalError"/> is set.</returns>
    public static EnvelopeDecodeResult Fatal(string error)
    {
        return new EnvelopeDecodeResult(Array.Empty<DecodedEnvelope>(), 0, error);
    }
}
