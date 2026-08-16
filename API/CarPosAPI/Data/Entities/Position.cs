namespace CarPosAPI.Data.Entities;

/// <summary>
/// One decrypted, validated GNSS fix. Rows are written exclusively by the ingest
/// pipeline's raw batched INSERT (<see cref="Services.Ingest.PositionWriter"/>) —
/// the entity exists so EF Core migrations own the schema and future read
/// endpoints can query it. The DB also carries a generated PostGIS
/// <c>location geography(Point,4326)</c> column deliberately absent from this
/// model: it is derived from latitude/longitude by the database itself, so the
/// app never needs a spatial dependency and the two can never drift apart.
/// Mapped by <see cref="Configurations.PositionConfiguration"/>.
/// </summary>
public sealed class Position
{
    /// <summary>Surrogate key (bigint identity).</summary>
    public long Id { get; set; }

    /// <summary>Owning device (FK, delete restricted — positions outlive nothing).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Navigation to the owning device.</summary>
    public Device? Device { get; set; }

    /// <summary>
    /// GNSS fix time (UTC, second precision — the firmware sends no milliseconds).
    /// Together with <see cref="DeviceId"/> this is the natural identity of a fix:
    /// the unique index on (device_id, fix_time) is what makes at-least-once MQTT
    /// delivery safe to replay.
    /// </summary>
    public DateTime FixTime { get; set; }

    /// <summary>Server arrival time (UTC, DB default now()) — backlogged fixes arrive late.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>Latitude in decimal degrees, +N/−S. CHECK-constrained to [-90, 90].</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude in decimal degrees, +E/−W. CHECK-constrained to [-180, 180].</summary>
    public double Longitude { get; set; }

    /// <summary>Ground speed in km/h as reported by the receiver.</summary>
    public double SpeedKmph { get; set; }

    /// <summary>Altitude above mean sea level in metres.</summary>
    public double AltitudeMeters { get; set; }

    /// <summary>
    /// Battery state of charge 0–100, or null when the device sent none (sensor
    /// disabled, read failed, or firmware predating the feature). The value 0 is
    /// a SENTINEL meaning "charging" — the FE renders it as such rather than as a
    /// flat battery. CHECK-constrained to [0, 100].
    /// </summary>
    public int? BatteryPct { get; set; }

    /// <summary>X-axis acceleration in g, or null when absent. CHECK-constrained to [-16, 16].</summary>
    public double? AccelXG { get; set; }

    /// <summary>Y-axis acceleration in g, or null when absent. CHECK-constrained to [-16, 16].</summary>
    public double? AccelYG { get; set; }

    /// <summary>Z-axis acceleration in g, or null when absent. CHECK-constrained to [-16, 16].</summary>
    public double? AccelZG { get; set; }

    /// <summary>
    /// Modem die temperature in °C at the fix (SIM7000 <c>AT+CPMUTEMP</c>), or null
    /// when the device sent none (older firmware, or the command unsupported). It
    /// is a proxy for how hot the tracker is running — a hot-car cut-off shows up
    /// here. CHECK-constrained to [-40, 125].
    /// </summary>
    public double? TemperatureC { get; set; }
}
