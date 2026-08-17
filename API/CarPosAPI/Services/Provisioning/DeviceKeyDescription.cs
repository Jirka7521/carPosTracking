namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// The device columns needed to re-render a firmware config file. It exists
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
/// <param name="Settings">
/// The values of the revision the device is meant to be running, fetched in the same
/// round trip by a correlated subquery so the rendered file's compile-time defaults
/// match what the broker is holding for it. Null for a device whose config row is
/// missing — a state only a hand-edited database can produce, handled by falling back
/// to the factory defaults rather than by failing the render.
/// </param>
internal sealed record DeviceKeyDescription(
    string? DisplayName,
    string? PublicKeyPem,
    string? AckPublicKeyPem,
    Dtos.DeviceConfigValuesDto? Settings);
