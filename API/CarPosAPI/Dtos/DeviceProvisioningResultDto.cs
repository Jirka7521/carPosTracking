namespace CarPosAPI.Dtos;

/// <summary>
/// Response body of a successful <c>POST /api/devices</c> — everything needed to
/// flash the firmware, and nothing more.
///
/// SECURITY: there is deliberately no private-key member here and there must
/// never be one. The generated private key is the receiver secret that keeps the
/// broker from reading positions; it is encrypted under the master key and stays
/// in the database (see <see cref="Services.Security.MasterKeyProtector"/>). The
/// device only ever needs the public half.
/// </summary>
/// <param name="DeviceId">The device's MQTT identity, exactly as stored.</param>
/// <param name="DisplayName">The optional friendly name, echoed back.</param>
/// <param name="TelemetryTopic">Topic the firmware publishes fixes to.</param>
/// <param name="ConfigTopic">Topic the firmware reads its retained settings from.</param>
/// <param name="BrokerUri">Broker URI the API itself is configured against.</param>
/// <param name="PublicKeyPem">Receiver RSA-3072 public key, PEM (SPKI) encoded.</param>
/// <param name="PublicKeyFingerprint">
/// SHA-256 of the SPKI bytes, uppercase hex — identifies the key in logs and
/// lets you confirm the flashed firmware carries the key this row expects,
/// without either side handling key material.
/// </param>
/// <param name="ConfigSnippet">
/// The above, pre-formatted as C++ <c>constexpr</c> lines ready to paste into
/// <c>ESP32/src/config/Config.h</c>.
/// </param>
public sealed record DeviceProvisioningResultDto(
    string DeviceId,
    string? DisplayName,
    string TelemetryTopic,
    string ConfigTopic,
    string BrokerUri,
    string PublicKeyPem,
    string PublicKeyFingerprint,
    string ConfigSnippet);
