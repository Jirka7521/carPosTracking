using CarPosAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Implements <see cref="IDeviceScheduleResolver"/>: one query, then the evaluator.
/// </summary>
internal sealed class DeviceScheduleResolver : IDeviceScheduleResolver
{
    private readonly CarPosDbContext _context;
    private readonly ScheduleEvaluator _evaluator;

    /// <summary>Creates the resolver.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="evaluator">The pure schedule arithmetic.</param>
    public DeviceScheduleResolver(CarPosDbContext context, ScheduleEvaluator evaluator)
    {
        _context = context;
        _evaluator = evaluator;
    }

    /// <inheritdoc />
    public async Task<ScheduleEvaluation> ResolveAsync(
        Guid deviceRowId,
        Guid? fallbackProfileId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Disabled rules are filtered in SQL rather than handed to the evaluator to
        // ignore. The evaluator's contract is "these rules are live", which keeps its
        // every code path about arithmetic and none of them about state.
        List<ScheduleRuleSnapshot> rules = await _context.DeviceConfigScheduleRules
            .AsNoTracking()
            .Where(rule => rule.DeviceId == deviceRowId && rule.IsEnabled)
            .Select(rule => new ScheduleRuleSnapshot(
                rule.Id,
                rule.ProfileId,
                rule.DaysMaskUtc,
                rule.StartMinuteUtc,
                rule.DurationMinutes,
                rule.Priority,
                rule.CreatedAt))
            .ToListAsync(cancellationToken);

        return _evaluator.Evaluate(rules, fallbackProfileId, utcNow);
    }
}
