using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// The verdict on a candidate ack public key: either a fingerprint to store it under,
/// or the reason it was refused. Never both.
/// </summary>
/// <param name="Failure">
/// Why the key is unusable — a code for the dashboard to translate and a sentence for
/// whoever supplied it, a dashboard user or the operator running the CLI — or null
/// when it is acceptable.
/// </param>
/// <param name="Fingerprint">
/// SHA-256 over the DER SubjectPublicKeyInfo, uppercase hex; null when
/// <paramref name="Failure"/> is set.
/// </param>
internal sealed record AckPublicKeyValidation(ServiceError? Failure, string? Fingerprint)
{
    /// <summary>True when the key may be stored.</summary>
    public bool IsValid => Failure is null;

    /// <summary>The English reason the key was refused, or null when it was not.</summary>
    public string? Error => Failure?.Detail;
}
