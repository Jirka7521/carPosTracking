namespace CarPosAPI.Services.Ingest;

/// <summary>
/// What a status message becomes in <c>device_events</c>: the stored kind, reason and
/// severity, already in the API's wire vocabulary. Produced only by
/// <see cref="DeviceEventClassifier"/>.
/// </summary>
/// <param name="Kind">A <see cref="Dtos.DeviceEventKindNames"/> value.</param>
/// <param name="Reason">A <see cref="Dtos.DeviceEventReasonNames"/> value.</param>
/// <param name="Severity">A <see cref="Dtos.DeviceEventSeverityNames"/> value.</param>
internal sealed record DeviceEventClassification(string Kind, string Reason, string Severity);
