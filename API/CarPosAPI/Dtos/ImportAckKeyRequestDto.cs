using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// The <em>public</em> half of an ack key pair the operator has just generated for a
/// device, on its way into storage.
///
/// <para>
/// The direction is what makes this endpoint safe to expose at all. For telemetry the
/// device encrypts and the server decrypts, so the server holds the private key; for
/// acks the server encrypts and the <em>device</em> decrypts, so the server holds only
/// this public half. The matching private key is generated in the operator's browser
/// and woven into the firmware's <c>Config.h</c> there — it never reaches this API, and
/// the service rejects a body that looks like it might contain one.
/// </para>
///
/// <para>
/// This is the API-driven equivalent of <c>import-device-key --ack-public-pem</c>,
/// which remains the route for someone working on the server with a shell.
/// </para>
/// </summary>
/// <param name="AckPublicKeyPem">
/// An RSA-3072 public key in SPKI PEM form (<c>-----BEGIN PUBLIC KEY-----</c>). The
/// length bounds are a cheap sanity gate, not the validation: the service parses the
/// PEM, checks the key size, and refuses anything carrying private-key material. An
/// RSA-3072 SPKI PEM is around 630 characters, so the ceiling leaves generous room for
/// line-ending and trailing-whitespace variation without accepting a payload that could
/// only be something else.
/// </param>
public sealed record ImportAckKeyRequestDto(
    [Required]
    [StringLength(4096, MinimumLength = 100)]
    string AckPublicKeyPem);
