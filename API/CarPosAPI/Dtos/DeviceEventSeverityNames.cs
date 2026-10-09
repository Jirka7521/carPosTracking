namespace CarPosAPI.Dtos;

/// <summary>
/// The wire spellings of how much a device event matters, lowest first. Decided on
/// the server alone (<c>Services.Ingest.DeviceEventClassifier</c>) — the firmware only
/// says what happened, never how worrying it is, so the two cannot disagree.
/// </summary>
public static class DeviceEventSeverityNames
{
    /// <summary>Expected behaviour: a planned sleep, a switch-off, a power-on.</summary>
    public const string Normal = "normal";

    /// <summary>Needs attention but is not a fault: a low battery, a brown-out.</summary>
    public const string Alert = "alert";

    /// <summary>Something went wrong: a crash, a caught fault, a lost connection.</summary>
    public const string Error = "error";

    /// <summary>
    /// The severities at or above <paramref name="minimum"/>, for a "this and worse"
    /// filter that runs as <c>severity IN (…)</c> in SQL. Null or an unknown value
    /// answers every severity, which is what "no filter" means.
    /// </summary>
    /// <param name="minimum">One of the constants above, or null.</param>
    /// <returns>The matching severities, lowest first.</returns>
    public static IReadOnlyList<string> AtLeast(string? minimum)
    {
        return minimum switch
        {
            Error => [Error],
            Alert => [Alert, Error],
            _ => [Normal, Alert, Error],
        };
    }
}
