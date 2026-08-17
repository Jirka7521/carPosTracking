using System.Security.Cryptography;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Common;
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

    /// <summary>
    /// What a device with no configuration revision is rendered with — a device being
    /// provisioned right now (its revision row is written after this service returns),
    /// or one whose row a hand-edited database has left behind. Immutable, so one
    /// shared instance is enough.
    /// </summary>
    private static readonly DeviceConfigValuesDto s_factoryDefaults = new DeviceConfigValuesDto(
        DeviceConfigRules.DefaultIntervalSeconds,
        DeviceConfigRules.DefaultSleepBetween,
        DeviceConfigRules.DefaultFixTimeoutSeconds,
        DeviceConfigRules.DefaultQueueMaxFixes,
        DeviceConfigRules.DefaultRetryIntervalHours,
        DeviceConfigRules.DefaultRetryMaxAgeHours,
        DeviceConfigRules.DefaultConfigCheckSeconds);

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
        // belongs to the device alone, so it is minted off-server — in the operator's
        // browser from the dashboard, or by hand — and only the public half is
        // imported. Until then the file says so and the device receives no acks.
        //
        // Factory defaults, because this device has no configuration revision yet:
        // the row that will carry them is written after this call returns.
        string snippet = _snippetBuilder.Build(
            device.DeviceId,
            _mqttOptions.BrokerUri,
            publicKeyPem,
            fingerprint,
            null,
            s_factoryDefaults,
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

        // Only the columns the file needs are selected. That is not merely tidiness:
        // never projecting PrivateKeyCiphertext is what keeps the device secret out
        // of this process's memory entirely.
        //
        // The settings come from a correlated subquery rather than a second round
        // trip — the same trick DeviceConfigService uses for revision authors.
        DeviceKeyDescription? description = await context.Devices
            .AsNoTracking()
            .Where(candidate => candidate.DeviceId == deviceId)
            .Select(candidate => new DeviceKeyDescription(
                candidate.DisplayName,
                candidate.PublicKeyPem,
                candidate.AckPublicKeyPem,
                context.DeviceConfigVersions
                    .Where(revision => revision.DeviceId == candidate.Id
                        && revision.Version == candidate.ConfigVersion)
                    .Select(revision => new DeviceConfigValuesDto(
                        revision.IntervalSeconds,
                        revision.SleepBetween,
                        revision.FixTimeoutSeconds,
                        revision.QueueMaxFixes,
                        revision.RetryIntervalHours,
                        revision.RetryMaxAgeHours,
                        revision.ConfigCheckSeconds))
                    .FirstOrDefault()))
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
        // comment it lands in describes when this file was rendered, and pretending
        // it is the original would hide that the device was provisioned long ago.
        string snippet = _snippetBuilder.Build(
            deviceId,
            _mqttOptions.BrokerUri,
            description.PublicKeyPem,
            fingerprint,
            ackFingerprint,
            description.Settings ?? s_factoryDefaults,
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

    /// <inheritdoc />
    public async Task<OperationResult<AckKeyImportedDto>> ImportAckPublicKeyAsync(
        CarPosDbContext context,
        string deviceId,
        string ackPublicKeyPem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(ackPublicKeyPem);

        // Validated before the database is touched, so a bad key cannot leave the row
        // half-updated. The rules live in AckPublicKeyValidator because the CLI import
        // path applies exactly the same ones.
        AckPublicKeyValidation validation = AckPublicKeyValidator.Validate(ackPublicKeyPem);

        if (!validation.IsValid)
        {
            return OperationResult<AckKeyImportedDto>.Invalid(validation.Error!);
        }

        // Tracked, not AsNoTracking: this one is a write.
        Device? device = await context.Devices
            .SingleOrDefaultAsync(row => row.DeviceId == deviceId, cancellationToken);

        if (device is null)
        {
            return OperationResult<AckKeyImportedDto>.NotFound("No such device.");
        }

        bool isRotation = !string.IsNullOrWhiteSpace(device.AckPublicKeyPem);

        device.AckPublicKeyPem = ackPublicKeyPem;
        await context.SaveChangesAsync(cancellationToken);

        string fingerprint = validation.Fingerprint!;

        // Logged at information level because it is a security-relevant change with a
        // blast radius: from here on the API seals every ack to this key, so a device
        // still carrying the old private half goes quiet until it is reflashed.
        _logger.LogInformation(
            "Stored ack public key (SPKI-SHA256 {Fingerprint}) for device {DeviceId}; rotation: {IsRotation}",
            fingerprint,
            deviceId,
            isRotation);

        return OperationResult<AckKeyImportedDto>.Success(new AckKeyImportedDto(fingerprint));
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
