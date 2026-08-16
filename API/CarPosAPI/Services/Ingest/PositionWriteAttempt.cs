namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Outcome of <see cref="IngestPipeline"/>'s bounded write-with-retry: what the
/// database did, and whether the batch was dropped as poison.
///
/// The poison flag exists because a constraint violation produces a zeroed
/// <see cref="PositionWriteResult"/> that is otherwise indistinguishable from a
/// successful write of nothing. Before delivery acks that ambiguity was harmless —
/// both paths simply consumed the message — but an ack must never report a poisoned
/// batch's fixes as "stored", or the device would delete data that never reached the
/// positions table.
/// </summary>
/// <param name="Result">
/// The write result, or null when the database was unreachable and the message must
/// be redelivered.
/// </param>
/// <param name="Poisoned">
/// True when the batch violated a constraint and was deliberately discarded.
/// </param>
internal sealed record PositionWriteAttempt(PositionWriteResult? Result, bool Poisoned);
