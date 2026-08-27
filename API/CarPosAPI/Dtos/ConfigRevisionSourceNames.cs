namespace CarPosAPI.Dtos;

/// <summary>
/// The wire spellings of <see cref="Data.Entities.ConfigRevisionSource"/>.
///
/// <para>
/// They are <c>const</c> rather than a converter because the one place that matters is
/// an EF Core projection: <c>DeviceConfigService.VersionProjection</c> turns the stored
/// enum into one of these inside a <c>Select</c>, and only a literal survives
/// translation to SQL. A <c>JsonStringEnumConverter</c> would have covered the
/// serialization half and left that half broken.
/// </para>
/// </summary>
public static class ConfigRevisionSourceNames
{
    /// <summary>A person saved it — or nobody did, for the rows predating schedules.</summary>
    public const string Manual = "manual";

    /// <summary>The scheduler applied a profile.</summary>
    public const string Schedule = "schedule";
}
