using System.Text.Json;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Parses a raw MQTT payload into structurally valid <see cref="DecodedEnvelope"/>s.
/// This is the first line of defence against hostile input: strict JSON parsing,
/// hard size caps and exact base64 length checks all happen here, before any
/// expensive RSA work — so malformed or oversized garbage is rejected for the
/// cost of a parse, never a private-key operation.
/// </summary>
internal sealed class EnvelopeCodec
{
    /// <summary>The only envelope scheme the firmware produces; anything else is rejected.</summary>
    public const string ExpectedAlgorithm = "RSA-OAEP-SHA256+AES-256-GCM";

    /// <summary>RSA-3072 output size — the wrapped AES key is always exactly this long.</summary>
    public const int WrappedKeyBytes = 384;

    /// <summary>Firmware GCM nonce size in bytes.</summary>
    public const int NonceBytes = 12;

    /// <summary>Firmware GCM tag size in bytes.</summary>
    public const int TagBytes = 16;

    /// <summary>
    /// Strict parser settings: no comments, no trailing commas, shallow depth.
    /// The payload is a flat array of flat objects — depth 4 already allows slack.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
    {
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 4,
    };

    private readonly IngestOptions _options;

    /// <summary>Creates the codec with the configured hardening limits.</summary>
    /// <param name="options">Validated ingest limits.</param>
    public EnvelopeCodec(IOptions<IngestOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Decodes one MQTT message payload.</summary>
    /// <param name="payload">Raw message bytes (UTF-8 JSON array of envelopes).</param>
    /// <returns>Valid envelopes plus counts; see <see cref="EnvelopeDecodeResult"/>.</returns>
    public EnvelopeDecodeResult Decode(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length == 0)
        {
            return EnvelopeDecodeResult.Fatal("empty payload");
        }

        if (payload.Length > _options.MaxMessageBytes)
        {
            return EnvelopeDecodeResult.Fatal(
                $"payload of {payload.Length} bytes exceeds limit {_options.MaxMessageBytes}");
        }

        List<EncryptionEnvelopeDto>? envelopes;
        try
        {
            envelopes = JsonSerializer.Deserialize<List<EncryptionEnvelopeDto>>(payload.Span, s_jsonOptions);
        }
        catch (JsonException)
        {
            // The exception message may echo payload fragments — log only our own text.
            return EnvelopeDecodeResult.Fatal("payload is not a valid JSON envelope array");
        }

        if (envelopes is null || envelopes.Count == 0)
        {
            return EnvelopeDecodeResult.Fatal("payload is not a non-empty JSON array");
        }

        if (envelopes.Count > _options.MaxEnvelopesPerMessage)
        {
            // Firmware bursts are capped at 40; wildly more is not a backlog, it is abuse.
            return EnvelopeDecodeResult.Fatal(
                $"{envelopes.Count} envelopes exceed limit {_options.MaxEnvelopesPerMessage}");
        }

        List<DecodedEnvelope> decoded = new List<DecodedEnvelope>(envelopes.Count);
        int rejected = 0;
        foreach (EncryptionEnvelopeDto envelope in envelopes)
        {
            DecodedEnvelope? one = TryDecodeOne(envelope);
            if (one is null)
            {
                rejected++;
            }
            else
            {
                decoded.Add(one);
            }
        }

        return new EnvelopeDecodeResult(decoded, rejected, null);
    }

    /// <summary>
    /// Validates and base64-decodes a single envelope. Length checks are exact —
    /// the firmware always produces 384/12/16-byte fields, so any deviation is
    /// tampering or corruption, not a variant worth accommodating.
    /// </summary>
    /// <param name="envelope">The parsed envelope DTO.</param>
    /// <returns>The decoded envelope, or null when structurally invalid.</returns>
    private DecodedEnvelope? TryDecodeOne(EncryptionEnvelopeDto envelope)
    {
        if (!string.Equals(envelope.Algorithm, ExpectedAlgorithm, StringComparison.Ordinal))
        {
            return null;
        }

        byte[]? wrappedKey = TryFromBase64(envelope.WrappedKey);
        byte[]? nonce = TryFromBase64(envelope.Nonce);
        byte[]? ciphertext = TryFromBase64(envelope.Ciphertext);
        byte[]? tag = TryFromBase64(envelope.Tag);

        if (wrappedKey is null || nonce is null || ciphertext is null || tag is null)
        {
            return null;
        }

        if (wrappedKey.Length != WrappedKeyBytes
            || nonce.Length != NonceBytes
            || tag.Length != TagBytes
            || ciphertext.Length == 0
            || ciphertext.Length > _options.MaxCiphertextBytes)
        {
            return null;
        }

        return new DecodedEnvelope(wrappedKey, nonce, ciphertext, tag);
    }

    /// <summary>Base64-decodes a field, treating null/invalid input as absent.</summary>
    /// <param name="value">The base64 string, possibly null.</param>
    /// <returns>Decoded bytes, or null when missing or not valid base64.</returns>
    private static byte[]? TryFromBase64(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
