namespace CarPosAPI.Dtos;

/// <summary>
/// What the caller gets back after storing a device's new ack public key.
///
/// Only the fingerprint, deliberately: it is the one value that lets the operator
/// confirm the key the server now holds is the pair of the private key that went into
/// their <c>Config.h</c>, and it is the same string the provisioning payload and the
/// <c>import-device-key</c> CLI print. Echoing the key back would add nothing.
/// </summary>
/// <param name="AckPublicKeyFingerprint">
/// SHA-256 over the DER SubjectPublicKeyInfo, uppercase hex.
/// </param>
public sealed record AckKeyImportedDto(string AckPublicKeyFingerprint);
