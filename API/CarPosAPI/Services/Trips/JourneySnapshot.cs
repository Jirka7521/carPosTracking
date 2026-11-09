namespace CarPosAPI.Services.Trips;

/// <summary>A journey as the tree rules need it — plain data, no EF.</summary>
/// <param name="Id">Journey id.</param>
/// <param name="ParentId">Enclosing journey, or null at the top level.</param>
/// <param name="Name">Display name.</param>
/// <param name="RangeFrom">Inclusive range start, or null.</param>
/// <param name="RangeTo">Exclusive range end, or null.</param>
internal sealed record JourneySnapshot(Guid Id, Guid? ParentId, string Name, DateTime? RangeFrom, DateTime? RangeTo);
