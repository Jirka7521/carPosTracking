using System.Text.Json;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Privacy;

/// <summary>
/// Implements <see cref="IDataExportService"/> by streaming JSON with
/// <see cref="Utf8JsonWriter"/>.
///
/// <b>The rule that matters here is what must never appear.</b> A data export is
/// the one endpoint whose job is to hand over everything, which makes it the one
/// endpoint where a careless projection ships a password hash or a device's sealed
/// private key to whoever asked. The three entities that carry secrets, <c>User</c>,
/// <c>Device</c> and <c>ShareLink</c>, are therefore never written directly: each goes
/// through a dedicated export record that has no field to leak. The rest (positions,
/// configuration revisions) hold no secrets at all. <c>DataExportShapeTests</c> pins the
/// records down against the serialised bytes. Keep both when this shape changes.
///
/// Scoped: it holds the request's <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class DataExportService : IDataExportService
{
    /// <summary>
    /// Identifies the document shape, so a future format change is detectable by
    /// anything that consumed an older export.
    /// </summary>
    private const string ExportFormat = "carpos-user-export/1";

    /// <summary>
    /// How many position rows to write between flushes. The writer buffers, so
    /// without this a long history would sit in memory anyway - which is precisely
    /// what streaming was meant to avoid.
    /// </summary>
    private const int FlushEveryRows = 2000;

    private readonly CarPosDbContext _context;
    private readonly PrivacyOptions _privacy;
    private readonly ILogger<DataExportService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="privacy">Controller identity, echoed into the document header.</param>
    /// <param name="logger">Structured logger — receives counts and a user id, never data.</param>
    public DataExportService(
        CarPosDbContext context,
        IOptions<PrivacyOptions> privacy,
        ILogger<DataExportService> logger)
    {
        ArgumentNullException.ThrowIfNull(privacy);

        _context = context;
        _privacy = privacy.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WriteExportAsync(int userId, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // Indented because a human being is going to open this file and try to read
        // it. The size cost is real but secondary to the document being usable.
        JsonWriterOptions writerOptions = new JsonWriterOptions { Indented = true };

        await using Utf8JsonWriter writer = new Utf8JsonWriter(destination, writerOptions);

        writer.WriteStartObject();

        WriteHeader(writer);
        await WriteProfileAsync(writer, userId, cancellationToken);
        await WriteAliasesAsync(writer, userId, cancellationToken);
        await WriteGrantsAsync(writer, userId, cancellationToken);
        await WriteShareLinksAsync(writer, userId, cancellationToken);
        await WriteAuthoredConfigurationAsync(writer, userId, cancellationToken);

        long positionCount = await WriteDevicesAndPositionsAsync(writer, userId, cancellationToken);

        writer.WriteEndObject();

        await writer.FlushAsync(cancellationToken);

        _logger.LogInformation(
            "Exported personal data for user {UserId}: {PositionCount} position(s)",
            userId,
            positionCount);
    }

    /// <summary>Writes the document header — what this file is and who produced it.</summary>
    /// <param name="writer">The open JSON writer.</param>
    private void WriteHeader(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("export");
        writer.WriteString("format", ExportFormat);
        writer.WriteString("generatedAtUtc", DateTime.UtcNow);
        writer.WriteString("controller", _privacy.ControllerName);
        writer.WriteString("controllerContact", _privacy.ControllerContactEmail);
        writer.WriteString("currentPrivacyPolicyVersion", _privacy.PolicyVersion);
        writer.WriteString(
            "notice",
            "Everything carPosTracking holds about this account, exported under GDPR Art. 15 and 20. "
            + "The position history is complete and uncapped. Secrets - the password hash and device "
            + "private keys - are deliberately excluded: they are not personal data to port, and "
            + "copying them out of the system would only weaken it.");
        writer.WriteEndObject();
    }

    /// <summary>Writes the account's own profile row.</summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="userId">The account being exported.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    private async Task WriteProfileAsync(Utf8JsonWriter writer, int userId, CancellationToken cancellationToken)
    {
        // Note what is absent from the projection: PasswordHash. It is never
        // selected, so it cannot leak however this document is later reshaped.
        UserExportRow? user = await _context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new UserExportRow(
                candidate.Id,
                candidate.Email,
                candidate.FirstName,
                candidate.LastName,
                candidate.CreatedAt,
                candidate.PrivacyPolicyVersion,
                candidate.PrivacyPolicyAcceptedAt))
            .SingleOrDefaultAsync(cancellationToken);

        writer.WriteStartObject("profile");

        if (user is not null)
        {
            writer.WriteNumber("id", user.Id);
            writer.WriteString("email", user.Email);
            writer.WriteString("firstName", user.FirstName);
            writer.WriteString("lastName", user.LastName);
            writer.WriteString("createdAtUtc", user.CreatedAt);
            writer.WriteString("privacyPolicyVersionAccepted", user.PrivacyPolicyVersion);
            WriteNullableDateTime(writer, "privacyPolicyAcceptedAtUtc", user.PrivacyPolicyAcceptedAt);
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes the caller's private nicknames for devices.</summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="userId">The account being exported.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    private async Task WriteAliasesAsync(Utf8JsonWriter writer, int userId, CancellationToken cancellationToken)
    {
        List<DeviceAliasExportRow> aliases = await _context.DeviceAliases
            .AsNoTracking()
            .Where(alias => alias.UserId == userId)
            .Join(
                _context.Devices,
                alias => alias.DeviceId,
                device => device.Id,
                (alias, device) => new DeviceAliasExportRow(device.DeviceId, alias.Alias, alias.UpdatedAt))
            .ToListAsync(cancellationToken);

        writer.WriteStartArray("deviceNicknames");

        foreach (DeviceAliasExportRow alias in aliases)
        {
            writer.WriteStartObject();
            writer.WriteString("deviceId", alias.DeviceId);
            writer.WriteString("nickname", alias.Alias);
            writer.WriteString("updatedAtUtc", alias.UpdatedAt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes both directions of the sharing graph: what the caller may see, and
    /// what they handed to somebody else.
    /// </summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="userId">The account being exported.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    private async Task WriteGrantsAsync(Utf8JsonWriter writer, int userId, CancellationToken cancellationToken)
    {
        List<GrantExportRow> held = await QueryGrantsAsync(
            _context.Accesses.Where(access => access.UserId == userId),
            cancellationToken);

        // Grants this account issued TO other people. Part of the record of what the
        // user did — they typed that address in themselves — and already visible to
        // them in the sharing UI.
        List<GrantExportRow> issued = await QueryGrantsAsync(
            _context.Accesses.Where(access => access.GrantedBy == userId && access.UserId != userId),
            cancellationToken);

        WriteGrantArray(writer, "accessGrantsHeld", held);
        WriteGrantArray(writer, "accessGrantsIssuedToOthers", issued);
    }

    /// <summary>Projects an already-filtered access query into export rows.</summary>
    /// <param name="query">The filtered access query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The projected rows.</returns>
    private async Task<List<GrantExportRow>> QueryGrantsAsync(
        IQueryable<Access> query,
        CancellationToken cancellationToken)
    {
        return await query
            .AsNoTracking()
            .Join(
                _context.Devices,
                access => access.DeviceId,
                device => device.Id,
                (access, device) => new GrantExportRow(
                    device.DeviceId,
                    access.UserId,
                    access.GrantedBy,
                    access.IsActive,
                    access.DateRegistration,
                    access.CanRead,
                    access.CanDelete,
                    access.CanShare,
                    access.CanModifySettings))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Writes one named array of access grants.</summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="propertyName">Name of the JSON array.</param>
    /// <param name="grants">The rows to write.</param>
    private static void WriteGrantArray(
        Utf8JsonWriter writer,
        string propertyName,
        List<GrantExportRow> grants)
    {
        writer.WriteStartArray(propertyName);

        foreach (GrantExportRow grant in grants)
        {
            writer.WriteStartObject();
            writer.WriteString("deviceId", grant.DeviceId);
            writer.WriteNumber("userId", grant.UserId);

            if (grant.GrantedBy.HasValue)
            {
                writer.WriteNumber("grantedByUserId", grant.GrantedBy.Value);
            }
            else
            {
                writer.WriteNull("grantedByUserId");
            }

            writer.WriteBoolean("isActive", grant.IsActive);
            writer.WriteString("grantedAtUtc", grant.GrantedAt);
            writer.WriteBoolean("canRead", grant.CanRead);
            writer.WriteBoolean("canDelete", grant.CanDelete);
            writer.WriteBoolean("canShare", grant.CanShare);
            writer.WriteBoolean("canModifySettings", grant.CanModifySettings);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes the temporary share links this account created.
    ///
    /// They belong in an Art. 15 export because they are a record of disclosure:
    /// this account made somebody's movements visible to a person outside the
    /// system, for a stated window, and that is exactly the kind of fact a data
    /// subject is entitled to see. What is <em>not</em> here is who opened them —
    /// this system stores no address, agent or identity for a share visitor, so the
    /// counters below are the whole of what was ever recorded.
    /// </summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="userId">The account being exported.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    private async Task WriteShareLinksAsync(Utf8JsonWriter writer, int userId, CancellationToken cancellationToken)
    {
        // Joined to devices so the export names the MQTT identity rather than an
        // internal Guid, exactly as the grant and nickname projections do. The three
        // secret columns are not selected.
        List<ShareLinkExportRow> links = await _context.ShareLinks
            .AsNoTracking()
            .Where(link => link.CreatedByUserId == userId)
            .Join(
                _context.Devices.AsNoTracking(),
                link => link.DeviceId,
                device => device.Id,
                (link, device) => new ShareLinkExportRow(
                    device.DeviceId,
                    link.Label,
                    link.ValidFrom,
                    link.ValidUntil,
                    link.Scope == ShareScope.FullTrack ? ShareScopeNames.FullTrack : ShareScopeNames.LatestOnly,
                    link.IncludeSpeed,
                    link.IncludeTelemetry,
                    link.CreatedAt,
                    link.RevokedAt,
                    link.SuccessfulRedeems,
                    link.LastAccessedAt))
            .OrderByDescending(link => link.CreatedAt)
            .ToListAsync(cancellationToken);

        writer.WriteStartArray("shareLinksCreated");

        foreach (ShareLinkExportRow link in links)
        {
            writer.WriteStartObject();
            writer.WriteString("deviceId", link.DeviceId);
            writer.WriteString("label", link.Label);
            writer.WriteString("validFromUtc", link.ValidFrom);
            writer.WriteString("validUntilUtc", link.ValidUntil);
            writer.WriteString("scope", link.Scope);
            writer.WriteBoolean("includeSpeed", link.IncludeSpeed);
            writer.WriteBoolean("includeTelemetry", link.IncludeTelemetry);
            writer.WriteString("createdAtUtc", link.CreatedAt);

            if (link.RevokedAt.HasValue)
            {
                writer.WriteString("revokedAtUtc", link.RevokedAt.Value);
            }
            else
            {
                writer.WriteNull("revokedAtUtc");
            }

            writer.WriteNumber("timesOpened", link.SuccessfulRedeems);

            if (link.LastAccessedAt.HasValue)
            {
                writer.WriteString("lastOpenedAtUtc", link.LastAccessedAt.Value);
            }
            else
            {
                writer.WriteNull("lastOpenedAtUtc");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes the configuration the caller authored: sampling profiles, schedule
    /// rules, and the revision history entries stamped with their id.
    /// </summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="userId">The account being exported.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    private async Task WriteAuthoredConfigurationAsync(
        Utf8JsonWriter writer,
        int userId,
        CancellationToken cancellationToken)
    {
        // Sampling policy is personal data by implication: it says how closely, and
        // on what weekly pattern, a vehicle is watched.
        List<DeviceConfigProfile> profiles = await _context.DeviceConfigProfiles
            .AsNoTracking()
            .Where(profile => profile.CreatedByUserId == userId)
            .OrderBy(profile => profile.CreatedAt)
            .ToListAsync(cancellationToken);

        writer.WriteStartArray("configurationProfilesAuthored");

        foreach (DeviceConfigProfile profile in profiles)
        {
            writer.WriteStartObject();
            writer.WriteString("name", profile.Name);
            writer.WriteNumber("scheduleSlot", profile.ScheduleSlot);
            writer.WriteNumber("intervalSeconds", profile.IntervalSeconds);
            writer.WriteBoolean("sleepBetween", profile.SleepBetween);
            writer.WriteNumber("fixTimeoutSeconds", profile.FixTimeoutSeconds);
            writer.WriteNumber("queueMaxFixes", profile.QueueMaxFixes);
            writer.WriteNumber("retryIntervalHours", profile.RetryIntervalHours);
            writer.WriteNumber("retryMaxAgeHours", profile.RetryMaxAgeHours);
            writer.WriteNumber("configCheckSeconds", profile.ConfigCheckSeconds);
            writer.WriteString("createdAtUtc", profile.CreatedAt);
            writer.WriteString("updatedAtUtc", profile.UpdatedAt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        List<DeviceConfigVersion> revisions = await _context.DeviceConfigVersions
            .AsNoTracking()
            .Where(revision => revision.CreatedByUserId == userId)
            .OrderBy(revision => revision.CreatedAt)
            .ToListAsync(cancellationToken);

        writer.WriteStartArray("configurationRevisionsAuthored");

        foreach (DeviceConfigVersion revision in revisions)
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", revision.Version);
            writer.WriteNumber("intervalSeconds", revision.IntervalSeconds);
            writer.WriteBoolean("sleepBetween", revision.SleepBetween);
            writer.WriteNumber("fixTimeoutSeconds", revision.FixTimeoutSeconds);
            writer.WriteNumber("queueMaxFixes", revision.QueueMaxFixes);
            writer.WriteNumber("retryIntervalHours", revision.RetryIntervalHours);
            writer.WriteNumber("retryMaxAgeHours", revision.RetryMaxAgeHours);
            writer.WriteNumber("configCheckSeconds", revision.ConfigCheckSeconds);
            writer.WriteString("source", revision.Source.ToString());
            writer.WriteString("createdAtUtc", revision.CreatedAt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes every device the caller may read, each with its complete position
    /// history.
    ///
    /// The positions are streamed one row at a time with
    /// <see cref="IQueryable"/>.<c>AsAsyncEnumerable</c> rather than materialised:
    /// this is the only place in the application that deliberately reads an unbounded
    /// number of position rows, and buffering a year of fixes to hand out a copy of
    /// them would be a self-inflicted outage.
    /// </summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="userId">The account being exported.</param>
    /// <param name="cancellationToken">Cancels the queries and the write.</param>
    /// <returns>How many position rows were written, for the log line.</returns>
    private async Task<long> WriteDevicesAndPositionsAsync(
        Utf8JsonWriter writer,
        int userId,
        CancellationToken cancellationToken)
    {
        // The device list is small and is read first, in full: streaming positions
        // per device below means a second reader on the same connection, which Npgsql
        // does not allow while this query is still open.
        List<DeviceExportRow> devices = await _context.Accesses
            .AsNoTracking()
            .Where(access => access.UserId == userId && access.IsActive && access.CanRead)
            .Join(
                _context.Devices,
                access => access.DeviceId,
                device => device.Id,
                (access, device) => new DeviceExportRow(
                    device.Id,
                    device.DeviceId,
                    device.DisplayName,
                    device.IsActive,
                    device.CreatedAt,
                    device.LastSeenAt,
                    device.TrackingDeclarationAcceptedAt))
            .OrderBy(device => device.DeviceId)
            .ToListAsync(cancellationToken);

        long total = 0;

        writer.WriteStartArray("devices");

        foreach (DeviceExportRow device in devices)
        {
            writer.WriteStartObject();
            writer.WriteString("deviceId", device.DeviceId);
            writer.WriteString("displayName", device.DisplayName);
            writer.WriteBoolean("isActive", device.IsActive);
            writer.WriteString("registeredAtUtc", device.CreatedAt);
            WriteNullableDateTime(writer, "lastSeenAtUtc", device.LastSeenAt);
            WriteNullableDateTime(
                writer,
                "trackingDeclarationAcceptedAtUtc",
                device.TrackingDeclarationAcceptedAt);

            total += await WritePositionsAsync(writer, device.RowId, cancellationToken);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        return total;
    }

    /// <summary>Streams one device's whole position history into the open object.</summary>
    /// <param name="writer">The open JSON writer, positioned inside a device object.</param>
    /// <param name="deviceRowId">Internal device id to filter on.</param>
    /// <param name="cancellationToken">Cancels the query and the write.</param>
    /// <returns>How many rows were written.</returns>
    private async Task<long> WritePositionsAsync(
        Utf8JsonWriter writer,
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        long written = 0;

        writer.WriteStartArray("positions");

        IAsyncEnumerable<Position> stream = _context.Positions
            .AsNoTracking()
            .Where(position => position.DeviceId == deviceRowId)
            .OrderBy(position => position.FixTime)
            .AsAsyncEnumerable();

        await foreach (Position position in stream.WithCancellation(cancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteString("fixTimeUtc", position.FixTime);
            writer.WriteString("receivedAtUtc", position.ReceivedAt);
            writer.WriteNumber("latitude", position.Latitude);
            writer.WriteNumber("longitude", position.Longitude);
            writer.WriteNumber("speedKmph", position.SpeedKmph);
            writer.WriteNumber("altitudeMeters", position.AltitudeMeters);
            WriteNullableInt(writer, "batteryPct", position.BatteryPct);
            WriteNullableDouble(writer, "accelXG", position.AccelXG);
            WriteNullableDouble(writer, "accelYG", position.AccelYG);
            WriteNullableDouble(writer, "accelZG", position.AccelZG);
            WriteNullableDouble(writer, "temperatureC", position.TemperatureC);
            writer.WriteEndObject();

            written++;

            // The writer buffers; without periodic flushes a long history would be
            // held in memory anyway, which is the thing streaming was meant to avoid.
            if (written % FlushEveryRows == 0)
            {
                await writer.FlushAsync(cancellationToken);
            }
        }

        writer.WriteEndArray();

        return written;
    }

    /// <summary>Writes a nullable timestamp, as a value or an explicit null.</summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="propertyName">Name of the JSON property.</param>
    /// <param name="value">The timestamp, or null.</param>
    private static void WriteNullableDateTime(Utf8JsonWriter writer, string propertyName, DateTime? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    /// <summary>Writes a nullable integer, as a value or an explicit null.</summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="propertyName">Name of the JSON property.</param>
    /// <param name="value">The number, or null.</param>
    private static void WriteNullableInt(Utf8JsonWriter writer, string propertyName, int? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    /// <summary>Writes a nullable double, as a value or an explicit null.</summary>
    /// <param name="writer">The open JSON writer.</param>
    /// <param name="propertyName">Name of the JSON property.</param>
    /// <param name="value">The number, or null.</param>
    private static void WriteNullableDouble(Utf8JsonWriter writer, string propertyName, double? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }
}
