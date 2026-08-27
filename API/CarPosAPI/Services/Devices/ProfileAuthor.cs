namespace CarPosAPI.Services.Devices;

/// <summary>
/// A profile id paired with the display name of whoever created it.
///
/// <para>
/// A named record purely because this project does not use <c>var</c> and therefore
/// cannot use an anonymous type for the LINQ projection that produces it — see the
/// project's CLAUDE.md, rule 3. It exists only between the query in
/// <see cref="DeviceConfigScheduleService"/> and the dictionary it is folded into.
/// </para>
/// </summary>
/// <param name="ProfileId">The profile.</param>
/// <param name="DisplayName">Its author's full name, or null when that account is gone.</param>
internal sealed record ProfileAuthor(Guid ProfileId, string? DisplayName);
