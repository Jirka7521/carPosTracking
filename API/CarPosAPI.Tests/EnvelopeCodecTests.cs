using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarPosAPI.Options;
using CarPosAPI.Services.Ingest;

namespace CarPosAPI.Tests;

/// <summary>
/// Structural hardening tests for the first parsing stage: exact base64 length
/// gates, size caps and strictness. These paths face raw broker input, so every
/// malformed variant must come back as a clean rejection, never an exception.
/// The codec checks structure only, so envelopes here use random bytes of the
/// right (or deliberately wrong) lengths — no real crypto needed.
/// </summary>
public sealed class EnvelopeCodecTests
{
    /// <summary>Builds an envelope JSON object with controllable field lengths.</summary>
    /// <param name="algorithm">The alg field value.</param>
    /// <param name="wrappedKeyBytes">Length of k.</param>
    /// <param name="nonceBytes">Length of iv.</param>
    /// <param name="ciphertextBytes">Length of ct.</param>
    /// <param name="tagBytes">Length of tag.</param>
    /// <returns>The envelope JSON text.</returns>
    private static string BuildEnvelopeJson(
        string algorithm = EnvelopeCodec.ExpectedAlgorithm,
        int wrappedKeyBytes = 384,
        int nonceBytes = 12,
        int ciphertextBytes = 150,
        int tagBytes = 16)
    {
        string wrappedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(wrappedKeyBytes));
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(nonceBytes));
        string ciphertext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(ciphertextBytes));
        string tag = Convert.ToBase64String(RandomNumberGenerator.GetBytes(tagBytes));
        return $"{{\"alg\":{JsonSerializer.Serialize(algorithm)},\"k\":\"{wrappedKey}\",\"iv\":\"{nonce}\",\"ct\":\"{ciphertext}\",\"tag\":\"{tag}\"}}";
    }

    /// <summary>Creates a codec with default (or overridden) limits.</summary>
    /// <param name="options">Optional non-default limits.</param>
    /// <returns>The codec under test.</returns>
    private static EnvelopeCodec CreateCodec(IngestOptions? options = null)
    {
        return new EnvelopeCodec(Microsoft.Extensions.Options.Options.Create(options ?? new IngestOptions()));
    }

    /// <summary>UTF-8 bytes of a JSON array of the given envelope objects.</summary>
    /// <param name="envelopes">Envelope JSON fragments.</param>
    /// <returns>Payload bytes as they would arrive from the broker.</returns>
    private static byte[] AsPayload(params string[] envelopes)
    {
        return Encoding.UTF8.GetBytes("[" + string.Join(",", envelopes) + "]");
    }

    [Fact]
    public void DecodesSingleValidEnvelope()
    {
        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(BuildEnvelopeJson()));

        Assert.Null(result.FatalError);
        Assert.Equal(0, result.RejectedEnvelopes);
        DecodedEnvelope envelope = Assert.Single(result.Envelopes);
        Assert.Equal(384, envelope.WrappedKey.Length);
        Assert.Equal(12, envelope.Nonce.Length);
        Assert.Equal(16, envelope.Tag.Length);
    }

    [Fact]
    public void DecodesBacklogBurstOfEnvelopes()
    {
        string[] envelopes = Enumerable.Range(0, 40).Select(_ => BuildEnvelopeJson()).ToArray();

        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(envelopes));

        Assert.Null(result.FatalError);
        Assert.Equal(40, result.Envelopes.Count);
    }

    [Fact]
    public void RejectsNonArrayPayloadAsFatal()
    {
        EnvelopeDecodeResult result = CreateCodec().Decode(Encoding.UTF8.GetBytes("{\"alg\":\"x\"}"));

        Assert.NotNull(result.FatalError);
        Assert.Empty(result.Envelopes);
    }

    [Fact]
    public void RejectsEmptyArrayAsFatal()
    {
        EnvelopeDecodeResult result = CreateCodec().Decode(Encoding.UTF8.GetBytes("[]"));

        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public void RejectsGarbageBytesAsFatal()
    {
        EnvelopeDecodeResult result = CreateCodec().Decode(RandomNumberGenerator.GetBytes(64));

        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public void RejectsTooManyEnvelopesAsFatal()
    {
        IngestOptions options = new IngestOptions { MaxEnvelopesPerMessage = 2 };
        string[] envelopes = { BuildEnvelopeJson(), BuildEnvelopeJson(), BuildEnvelopeJson() };

        EnvelopeDecodeResult result = CreateCodec(options).Decode(AsPayload(envelopes));

        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public void RejectsOversizedMessageAsFatal()
    {
        IngestOptions options = new IngestOptions { MaxMessageBytes = 1024 };
        string[] envelopes = { BuildEnvelopeJson(ciphertextBytes: 2048) };

        EnvelopeDecodeResult result = CreateCodec(options).Decode(AsPayload(envelopes));

        Assert.NotNull(result.FatalError);
    }

    [Theory]
    [InlineData("wrong-alg")]
    [InlineData("")]
    public void RejectsWrongAlgorithmEnvelope(string algorithm)
    {
        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(BuildEnvelopeJson(algorithm: algorithm)));

        Assert.Null(result.FatalError);
        Assert.Empty(result.Envelopes);
        Assert.Equal(1, result.RejectedEnvelopes);
    }

    [Theory]
    [InlineData(383)]
    [InlineData(385)]
    public void RejectsWrongWrappedKeyLength(int wrappedKeyBytes)
    {
        EnvelopeDecodeResult result = CreateCodec()
            .Decode(AsPayload(BuildEnvelopeJson(wrappedKeyBytes: wrappedKeyBytes)));

        Assert.Equal(1, result.RejectedEnvelopes);
        Assert.Empty(result.Envelopes);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    public void RejectsWrongNonceLength(int nonceBytes)
    {
        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(BuildEnvelopeJson(nonceBytes: nonceBytes)));

        Assert.Equal(1, result.RejectedEnvelopes);
        Assert.Empty(result.Envelopes);
    }

    [Fact]
    public void RejectsWrongTagLength()
    {
        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(BuildEnvelopeJson(tagBytes: 15)));

        Assert.Equal(1, result.RejectedEnvelopes);
        Assert.Empty(result.Envelopes);
    }

    [Fact]
    public void RejectsOversizedCiphertextEnvelope()
    {
        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(BuildEnvelopeJson(ciphertextBytes: 4097)));

        Assert.Equal(1, result.RejectedEnvelopes);
        Assert.Empty(result.Envelopes);
    }

    [Fact]
    public void RejectsInvalidBase64Envelope()
    {
        string envelope = "{\"alg\":\"" + EnvelopeCodec.ExpectedAlgorithm + "\",\"k\":\"!!!\",\"iv\":\"AAAA\",\"ct\":\"AAAA\",\"tag\":\"AAAA\"}";

        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(envelope));

        Assert.Equal(1, result.RejectedEnvelopes);
        Assert.Empty(result.Envelopes);
    }

    [Fact]
    public void RejectsMissingFieldEnvelope()
    {
        string envelope = "{\"alg\":\"" + EnvelopeCodec.ExpectedAlgorithm + "\",\"k\":\"AAAA\",\"ct\":\"AAAA\",\"tag\":\"AAAA\"}";

        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(envelope));

        Assert.Equal(1, result.RejectedEnvelopes);
        Assert.Empty(result.Envelopes);
    }

    [Fact]
    public void KeepsValidEnvelopesWhenOneIsMalformed()
    {
        // One poison envelope must never sink its batch-mates.
        EnvelopeDecodeResult result = CreateCodec().Decode(
            AsPayload(BuildEnvelopeJson(), BuildEnvelopeJson(nonceBytes: 5), BuildEnvelopeJson()));

        Assert.Null(result.FatalError);
        Assert.Equal(2, result.Envelopes.Count);
        Assert.Equal(1, result.RejectedEnvelopes);
    }

    [Fact]
    public void ToleratesUnknownJsonMembers()
    {
        // Future firmware fields must not brick ingestion.
        string envelope = BuildEnvelopeJson().TrimEnd('}') + ",\"future_field\":123}";

        EnvelopeDecodeResult result = CreateCodec().Decode(AsPayload(envelope));

        Assert.Null(result.FatalError);
        Assert.Single(result.Envelopes);
    }
}
