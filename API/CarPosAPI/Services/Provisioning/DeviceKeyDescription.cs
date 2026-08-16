namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// The two device columns needed to re-render a firmware config block. It exists
/// as a named type purely so the projection in
/// <see cref="DeviceProvisioningService.DescribeAsync"/> can stay an explicit
/// <c>Select</c> instead of an anonymous type — and, more importantly, so that
/// projection can never quietly widen to include the private key ciphertext.
/// </summary>
/// <param name="DisplayName">The device's shared friendly name, if any.</param>
/// <param name="PublicKeyPem">Receiver RSA-3072 public key in SPKI PEM form, if stored.</param>
/// <param name="AckPublicKeyPem">
/// The device's ack public key in SPKI PEM form, if one has been imported. Public
/// by construction — the ack private key is never stored server-side — so widening
/// the projection to include it does not weaken the guarantee this type exists for.
/// </param>
internal sealed record DeviceKeyDescription(
    string? DisplayName,
    string? PublicKeyPem,
    string? AckPublicKeyPem);
