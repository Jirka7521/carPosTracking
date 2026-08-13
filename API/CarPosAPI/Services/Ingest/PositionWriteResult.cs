namespace CarPosAPI.Services.Ingest;

/// <summary>
/// What one batched insert achieved. Duplicates are normal, not errors: MQTT
/// QoS 2 plus SD-card backlog replay makes redelivery of already-stored fixes an
/// expected event, absorbed by ON CONFLICT DO NOTHING.
/// </summary>
/// <param name="Inserted">Rows actually written.</param>
/// <param name="Duplicates">Fixes skipped as already present (in the batch or the table).</param>
internal sealed record PositionWriteResult(int Inserted, int Duplicates);
