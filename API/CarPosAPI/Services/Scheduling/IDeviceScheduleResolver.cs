namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Loads one device's rules and asks <see cref="ScheduleEvaluator"/> what they mean
/// right now.
///
/// <para>
/// The seam between the pure arithmetic and the database. Two callers need it — the
/// schedule endpoints, which render the answer, and the manual settings save, which
/// needs the next boundary to stamp an override at — and neither should be reaching for
/// rule rows itself.
/// </para>
///
/// <para>
/// Deliberately <b>not</b> used by the reconciling worker: that works over the whole
/// fleet in one pass, and calling a per-device resolver in a loop is exactly the N+1
/// this project's rules forbid. It loads rules for every scheduled device at once and
/// calls the evaluator directly.
/// </para>
///
/// Scoped — it reads through the request's <c>CarPosDbContext</c>.
/// </summary>
internal interface IDeviceScheduleResolver
{
    /// <summary>
    /// Resolves the device's schedule at <paramref name="utcNow"/>.
    /// </summary>
    /// <param name="deviceRowId">Internal device id; the caller has already authorised it.</param>
    /// <param name="fallbackProfileId">The schedule's fallback profile, from the device row.</param>
    /// <param name="utcNow">The instant to evaluate at.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The evaluation. Disabled rules are excluded before it is computed.</returns>
    Task<ScheduleEvaluation> ResolveAsync(
        Guid deviceRowId,
        Guid? fallbackProfileId,
        DateTime utcNow,
        CancellationToken cancellationToken);
}
