namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Brings every scheduled device into line with what its rules say it should be running
/// right now.
///
/// <para>
/// <b>It reconciles; it does not fire.</b> Nothing here asks "did a boundary just
/// pass?" — it asks "is this device running the right values?", and acts only when the
/// answer is no. That difference is what makes the whole feature robust to the things
/// that actually happen to a background worker: a pass that ran late, a process that
/// was restarted over a boundary, two passes in the same second, a broker that was down
/// when the last change went out. All of them converge; none of them need special
/// handling.
/// </para>
///
/// <para>
/// Scoped, because it needs a <c>CarPosDbContext</c> and the scoped revision writer.
/// <see cref="DeviceConfigScheduleWorker"/> opens a scope per pass to reach it.
/// </para>
/// </summary>
internal interface IScheduleReconciler
{
    /// <summary>
    /// Runs one pass over the whole fleet.
    /// </summary>
    /// <param name="utcNow">The instant to evaluate every schedule at.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many devices had their settings changed — usually zero.</returns>
    Task<int> ReconcileAsync(DateTime utcNow, CancellationToken cancellationToken);
}
