using System.Security.Cryptography;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// Generates a device's RSA-3072 key pair, stores the private half encrypted at
/// rest and returns everything needed to flash the firmware.
///
/// The split of secrets is the whole point of the class: the private key is the
/// receiver secret that keeps the broker (and anyone who steals an SD card) from
/// reading positions, so it is protected with <see cref="IMasterKeyProtector"/>
/// and written straight to the database. Only the public half — plus topics and
/// the broker URI — is ever returned or logged.
///
/// Stateless: the context it works on is supplied by the caller, so the device
/// row can be committed together with the access grants that make it reachable.
/// </summary>
internal sealed class DeviceProvisioningService : IDeviceProvisioningService
{
    /// <summary>The only key size the firmware ecosystem uses.</summary>
    private const int ExpectedRsaKeySizeBits = 3072;

    private readonly IMasterKeyProtector _protector;
    private readonly ConfigSnippetBuilder _snippetBuilder;
    private readonly MqttOptions _mqttOptions;
    private readonly ILogger<DeviceProvisioningService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="protector">Encrypts the generated private key at rest.</param>
    /// <param name="snippetBuilder">Renders topics and the firmware config block.</param>
    /// <param name="mqttOptions">Supplies the broker URI echoed back to the caller.</param>
    /// <param name="logger">Structured logger (never receives key material).</param>
    public DeviceProvisioningService(
        IMasterKeyProtector protector,
        ConfigSnippetBuilder snippetBuilder,
        IOptions<MqttOptions> mqttOptions,
        ILogger<DeviceProvisioningService> logger)
    {
        _protector = protector;
        _snippetBuilder = snippetBuilder;
        _mqttOptions = mqttOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DeviceProvisioningResult> ProvisionAsync(
        CarPosDbContext context,
        CreateDeviceRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        // Cheap pre-check so the common "typo, device already exists" case answers
        // 409 without burning a key generation. It is not the real guard — the
        // unique index is, and the catch below closes the race between the two.
        bool exists = await context.Devices
            .AsNoTracking()
            .AnyAsync(candidate => candidate.DeviceId == request.DeviceId, cancellationToken);

        if (exists)
        {
            return DeviceProvisioningResult.DuplicateDeviceId();
        }

        // RSA-3072 generation is CPU-bound with no async form. It costs a fraction
        // of a second and this endpoint is called once per physical device, so it
        // runs inline rather than being pushed onto another thread-pool thread.
        using RSA key = RSA.Create(ExpectedRsaKeySizeBits);

        string publicKeyPem = key.ExportSubjectPublicKeyInfoPem();
        string privateKeyPem = key.ExportPkcs8PrivateKeyPem();

        // Bound to this device id as GCM associated data, so the blob cannot be
        // moved onto another device's row and still decrypt.
        byte[] protectedPrivateKey = _protector.Protect(privateKeyPem, request.DeviceId);

        Device device = new Device
        {
            DeviceId = request.DeviceId,
            DisplayName = request.DisplayName,
            PublicKeyPem = publicKeyPem,
            PrivateKeyCiphertext = protectedPrivateKey,
            IsActive = true,
        };

        context.Devices.Add(device);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Lost the race against a concurrent provisioning of the same id.
            // Expected, so it answers 409 like the pre-check rather than bubbling
            // up as a 500.
            return DeviceProvisioningResult.DuplicateDeviceId();
        }

        // SPKI-SHA256 identifies the key without revealing it — the same
        // fingerprint the import-device-key CLI prints.
        string fingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

        _logger.LogInformation(
            "Provisioned device {DeviceId} with a generated RSA-{KeySize} key pair (SPKI-SHA256 {Fingerprint})",
            device.DeviceId,
            ExpectedRsaKeySizeBits,
            fingerprint);

        // No ack key yet, and deliberately none generated here: the ack private key
        // belongs to the device alone, so the operator mints the pair off-server and
        // imports only the public half with import-device-key. Until they do, the
        // snippet says so and the device simply receives no delivery acks.
        string snippet = _snippetBuilder.Build(
            device.DeviceId,
            _mqttOptions.BrokerUri,
            publicKeyPem,
            fingerprint,
            null,
            DateTime.UtcNow);

        return DeviceProvisioningResult.Created(
            new DeviceProvisioningResultDto(
                device.DeviceId,
                device.DisplayName,
                _snippetBuilder.TelemetryTopicFor(device.DeviceId),
                _snippetBuilder.ConfigTopicFor(device.DeviceId),
                _snippetBuilder.AckTopicFor(device.DeviceId),
                _mqttOptions.BrokerUri,
                publicKeyPem,
                fingerprint,
                null,
                snippet),
            device.Id);
    }

    /// <inheritdoc />
    public async Task<DeviceProvisioningResultDto?> DescribeAsync(
        CarPosDbContext context,
        string deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(deviceId);

        // Only the two columns the snippet needs are selected. That is not merely
        // tidiness: never projecting PrivateKeyCiphertext is what keeps the device
        // secret out of this process's memory entirely.
        DeviceKeyDescription? description = await context.Devices
            .AsNoTracking()
            .Where(candidate => candidate.DeviceId == deviceId)
            .Select(candidate => new DeviceKeyDescription(
                candidate.DisplayName,
                candidate.PublicKeyPem,
                candidate.AckPublicKeyPem))
            .SingleOrDefaultAsync(cancellationToken);

        if (description is null || string.IsNullOrWhiteSpace(description.PublicKeyPem))
        {
            // No row, or a row imported before public keys were stored. Either way
            // there is nothing truthful to render, and inventing a snippet with a
            // missing key would produce firmware that silently cannot be decrypted.
            return null;
        }

        string fingerprint = ComputeSpkiFingerprint(description.PublicKeyPem);

        // Null when no ack key has been imported — a normal state, not an error, so
        // the snippet renders its "not yet configured" variant rather than failing.
        string? ackFingerprint = string.IsNullOrWhiteSpace(description.AckPublicKeyPem)
            ? null
            : ComputeSpkiFingerprint(description.AckPublicKeyPem);

        // The generation timestamp is "now" rather than the row's created_at: the
        // comment it lands in describes when this block was rendered, and pretending
        // it is the original would hide that the device was provisioned long ago.
        string snippet = _snippetBuilder.Build(
            deviceId,
            _mqttOptions.BrokerUri,
            description.PublicKeyPem,
            fingerprint,
            ackFingerprint,
            DateTime.UtcNow);

        return new DeviceProvisioningResultDto(
            deviceId,
            description.DisplayName,
            _snippetBuilder.TelemetryTopicFor(deviceId),
            _snippetBuilder.ConfigTopicFor(deviceId),
            _snippetBuilder.AckTopicFor(deviceId),
            _mqttOptions.BrokerUri,
            description.PublicKeyPem,
            fingerprint,
            ackFingerprint,
            snippet);
    }

    /// <summary>
    /// Recomputes the SPKI-SHA256 fingerprint of a stored public key, so the value
    /// shown later is byte-for-byte the one provisioning printed.
    /// </summary>
    /// <param name="publicKeyPem">The stored SPKI PEM.</param>
    /// <returns>Uppercase hex of the SHA-256 over the DER SubjectPublicKeyInfo.</returns>
    private static string ComputeSpkiFingerprint(string publicKeyPem)
    {
        using RSA key = RSA.Create();
        key.ImportFromPem(publicKeyPem);
        return Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    }
}
