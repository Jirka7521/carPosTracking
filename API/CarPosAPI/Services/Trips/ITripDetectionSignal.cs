namespace CarPosAPI.Services.Trips;

/// <summary>
/// The wake-up call between saving positions and detecting trips. Ingest calls
/// <see cref="Notify"/> after each insert; <see cref="TripDetectionWorker"/> waits on
/// <see cref="WaitAsync"/> and runs a pass straight away — so trips follow each save
/// instead of a timer.
///
/// <para>
/// <b>Only a signal, never data.</b> What is new is read from the database by the
/// pass (the position-id cursor), so a notification lost to a restart costs nothing:
/// the startup pass finds the same rows.
/// </para>
/// </summary>
internal interface ITripDetectionSignal
{
    /// <summary>
    /// Says "new data may be waiting". Never blocks and never throws, so it is safe on
    /// the ingest path; notifications that arrive while one is already pending merge
    /// into it.
    /// </summary>
    void Notify();

    /// <summary>Completes when a notification is pending, consuming it.</summary>
    /// <param name="cancellationToken">Cancels the wait without consuming anything.</param>
    /// <returns>A task that completes when notified.</returns>
    ValueTask WaitAsync(CancellationToken cancellationToken);
}
