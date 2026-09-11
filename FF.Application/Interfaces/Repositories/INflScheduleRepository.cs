// FF.Application/Interfaces/Repositories/INflScheduleRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

/// <summary>
/// Access to the NFL schedule (collection <c>nfl_schedule</c>, FAN-178).
///
/// Every team argument is normalised inside the implementation, so callers may
/// pass a Sleeper abbreviation, an nflverse one, or whatever a game log
/// happened to store, without having to know which.
/// </summary>
public interface INflScheduleRepository
{
    Task UpsertBatchAsync(
        IReadOnlyList<NflScheduleDocument> games,
        CancellationToken ct = default);

    Task<IReadOnlyList<NflScheduleDocument>> GetByWeekAsync(
        int season, int week, CancellationToken ct = default);

    Task<IReadOnlyList<NflScheduleDocument>> GetBySeasonAsync(
        int season, CancellationToken ct = default);

    /// <summary>
    /// The game a team plays in a given week, or null if it is on bye or the
    /// schedule has not been imported. Matches the team on either side.
    /// </summary>
    Task<NflScheduleDocument?> GetTeamGameAsync(
        string team, int season, int week, CancellationToken ct = default);

    /// <summary>
    /// How many games are stored for a season. Callers use this to refuse to
    /// operate on an un-imported schedule rather than silently treating an
    /// empty result as "everyone is on bye".
    /// </summary>
    Task<long> CountBySeasonAsync(int season, CancellationToken ct = default);

    /// <summary>
    /// The week the season is currently in: the week owning the next game still
    /// to be played, or 0 before the season opens.
    ///
    /// This is the data-driven replacement for the calendar heuristic in
    /// <c>NflContextService.CalcWeek</c> (FAN-139), which assumed week 1 begins
    /// on the first Thursday on or after September 1 and therefore rolled six
    /// days early into a 2026 season that opened on a Wednesday.
    ///
    /// Returns null when the season has no schedule rows at all, so the caller
    /// can fall back deliberately instead of reading 0 as "preseason".
    /// </summary>
    Task<int?> GetCurrentWeekAsync(
        int season, DateTime asOfUtc, CancellationToken ct = default);
}
