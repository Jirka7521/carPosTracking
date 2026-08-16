using CarPosAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Writes validated fixes with a single set-based
/// <c>INSERT … SELECT unnest(…) ON CONFLICT (device_id, fix_time) DO NOTHING</c>.
/// One round trip per MQTT message matters because the database is remote: a
/// 40-fix backlog burst done row-by-row would cost 40+ network round trips, and a
/// full 20 000-fix drain would crawl. The constant SQL text (arrays as
/// parameters) also lets Npgsql auto-prepare the statement. EF's SaveChanges
/// cannot express ON CONFLICT — a redelivered duplicate would abort the whole
/// batch — hence interpolated raw SQL, which EF turns into real parameters
/// (never string concatenation). Table/column names are hard-coded to match
/// <see cref="Data.Configurations.PositionConfiguration"/> exactly.
/// </summary>
internal sealed class PositionWriter : IPositionWriter
{
    private readonly IDbContextFactory<CarPosDbContext> _contextFactory;
    private readonly ILogger<PositionWriter> _logger;

    /// <summary>Creates the writer.</summary>
    /// <param name="contextFactory">Factory for short-lived DbContexts (singleton-safe).</param>
    /// <param name="logger">Structured logger (never receives coordinates).</param>
    public PositionWriter(IDbContextFactory<CarPosDbContext> contextFactory, ILogger<PositionWriter> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PositionWriteResult> WriteBatchAsync(
        Guid deviceId,
        IReadOnlyList<ValidatedPosition> positions,
        CancellationToken cancellationToken)
    {
        if (positions.Count == 0)
        {
            return new PositionWriteResult(0, 0);
        }

        // Dedupe inside the batch first (a live fix can ride along with a backlog
        // drain that already contains it), keeping the first occurrence.
        Dictionary<DateTime, ValidatedPosition> unique = new Dictionary<DateTime, ValidatedPosition>(positions.Count);
        foreach (ValidatedPosition position in positions)
        {
            if (!unique.ContainsKey(position.FixTimeUtc))
            {
                unique.Add(position.FixTimeUtc, position);
            }
        }

        DateTime[] fixTimes = new DateTime[unique.Count];
        double[] latitudes = new double[unique.Count];
        double[] longitudes = new double[unique.Count];
        double[] speeds = new double[unique.Count];
        double[] altitudes = new double[unique.Count];
        // Optional sensor columns: nullable element arrays so an absent reading
        // travels as a real SQL NULL through unnest (Npgsql maps int?[]→int4[] and
        // double?[]→float8[], NULLs preserved).
        int?[] batteries = new int?[unique.Count];
        double?[] accelXs = new double?[unique.Count];
        double?[] accelYs = new double?[unique.Count];
        double?[] accelZs = new double?[unique.Count];
        double?[] temperatures = new double?[unique.Count];

        int index = 0;
        foreach (ValidatedPosition position in unique.Values)
        {
            // Every element is DateTimeKind.Utc (guaranteed by the validator), which
            // Npgsql requires for timestamptz[] parameters.
            fixTimes[index] = position.FixTimeUtc;
            latitudes[index] = position.Latitude;
            longitudes[index] = position.Longitude;
            speeds[index] = position.SpeedKmph;
            altitudes[index] = position.AltitudeMeters;
            batteries[index] = position.BatteryPct;
            accelXs[index] = position.AccelXG;
            accelYs[index] = position.AccelYG;
            accelZs[index] = position.AccelZG;
            temperatures[index] = position.TemperatureC;
            index++;
        }

        await using CarPosDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // received_at is filled by its DB default now(). The conflict target names
        // the unique-index columns, so it keeps working even if the index is renamed.
        int inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO positions (device_id, fix_time, latitude, longitude, speed_kmph, altitude_m, battery_pct, accel_x_g, accel_y_g, accel_z_g, temperature_c)
             SELECT {deviceId}, batch.fix_time, batch.latitude, batch.longitude, batch.speed_kmph, batch.altitude_m, batch.battery_pct, batch.accel_x_g, batch.accel_y_g, batch.accel_z_g, batch.temperature_c
             FROM unnest(
                 {fixTimes}::timestamptz[],
                 {latitudes}::float8[],
                 {longitudes}::float8[],
                 {speeds}::float8[],
                 {altitudes}::float8[],
                 {batteries}::int4[],
                 {accelXs}::float8[],
                 {accelYs}::float8[],
                 {accelZs}::float8[],
                 {temperatures}::float8[])
                 AS batch(fix_time, latitude, longitude, speed_kmph, altitude_m, battery_pct, accel_x_g, accel_y_g, accel_z_g, temperature_c)
             ON CONFLICT (device_id, fix_time) DO NOTHING
             """,
            cancellationToken);

        // The device is demonstrably alive even when every fix was a duplicate, so
        // last_seen_at always advances. Server-side UtcNow avoids clock skew.
        await context.Devices
            .Where(device => device.Id == deviceId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(device => device.LastSeenAt, _ => DateTime.UtcNow),
                cancellationToken);

        int duplicates = positions.Count - inserted;
        return new PositionWriteResult(inserted, duplicates);
    }
}
