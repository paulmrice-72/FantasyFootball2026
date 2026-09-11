using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

/// <summary>
/// Resolves current NFL season and week.
///
/// <para>
/// Resolution order, highest first:
/// <list type="number">
///   <item>the admin simulation override, when one is set — a deliberate pin
///         always wins;</item>
///   <item>the imported schedule (FAN-178), which knows when games actually
///         are;</item>
///   <item><see cref="CalcWeek"/>, the calendar heuristic, as a last resort.</item>
/// </list>
/// </para>
///
/// <para>
/// FAN-139. Step 2 did not exist, so step 3 was the whole answer, and step 3 is
/// wrong: it assumes week 1 begins on the first Thursday on or after September 1.
/// For 2026 that computed Thursday 3 September against a season that opened on
/// Wednesday 9 September, so production silently moved to week 1 six days early —
/// no job, no deploy, no log line — and every consumer that queries by exact week
/// resolved nothing. The heuristic is also wrong in kind, not just by days: in a
/// normal year the opener is the Thursday <i>after Labor Day</i>, which is not the
/// first Thursday of the month whenever September 1 falls on a Tue/Wed/Thu.
/// </para>
///
/// <para>
/// The heuristic is kept rather than deleted because it is the only answer
/// available on a database with no schedule, and a wrong week degrades better
/// than a throw. It now says so when it fires.
/// </para>
/// </summary>
public class NflContextService(
    IAppSettingsRepository appSettingsRepo,
    INflScheduleRepository scheduleRepository,
    ILogger<NflContextService> logger) : INflContextService
{
    public async Task<int> GetSeasonAsync()
    {
        var settings = await appSettingsRepo.GetAsync();
        if (settings.SimulationSeasonOverride.HasValue)
            return settings.SimulationSeasonOverride.Value;

        return CalcSeason(DateTime.UtcNow);
    }

    public async Task<int> GetWeekAsync()
    {
        var settings = await appSettingsRepo.GetAsync();
        if (settings.SimulationWeekOverride.HasValue)
            return settings.SimulationWeekOverride.Value;

        var now = DateTime.UtcNow;
        var season = settings.SimulationSeasonOverride ?? CalcSeason(now);
        return await ResolveWeekAsync(season, now);
    }

    public async Task<(int Season, int Week)> GetContextAsync()
    {
        var settings = await appSettingsRepo.GetAsync();
        var now = DateTime.UtcNow;

        var season = settings.SimulationSeasonOverride ?? CalcSeason(now);

        if (settings.SimulationWeekOverride.HasValue)
            return (season, settings.SimulationWeekOverride.Value);

        return (season, await ResolveWeekAsync(season, now));
    }

    /// <summary>
    /// Schedule first, calendar heuristic only if there is no schedule at all.
    /// </summary>
    private async Task<int> ResolveWeekAsync(int season, DateTime now)
    {
        try
        {
            var fromSchedule = await scheduleRepository.GetCurrentWeekAsync(season, now);
            if (fromSchedule.HasValue) return fromSchedule.Value;

            logger.LogWarning(
                "No schedule imported for season {Season} — falling back to the calendar " +
                "heuristic, which is known to be wrong in some seasons (FAN-139). Run " +
                "POST /api/v1/admin/sync-nfl-schedule to resolve the week from real games.",
                season);
        }
        catch (Exception ex)
        {
            // The week is read on nearly every page. A schedule lookup failing
            // must degrade to the old behaviour, not take the site down.
            logger.LogError(ex,
                "Schedule lookup failed while resolving the week for season {Season} — " +
                "falling back to the calendar heuristic.", season);
        }

        return CalcWeek(now, season);
    }

    // ── Calendar fallback logic ───────────────────────────────────────────
    public static int CalcSeason(DateTime utcNow) =>
        utcNow.Month >= 3 ? utcNow.Year : utcNow.Year - 1;

    /// <summary>
    /// Calendar guess at the week, retained only as the no-schedule fallback.
    /// Prefer <see cref="INflScheduleRepository.GetCurrentWeekAsync"/>; see the
    /// class remarks for why this is wrong in kind and not merely imprecise.
    /// </summary>
    public static int CalcWeek(DateTime utcNow, int season)
    {
        var sept1 = new DateTime(season, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)sept1.DayOfWeek + 7) % 7;
        var seasonStart = sept1.AddDays(daysUntilThursday);
        if (utcNow < seasonStart) return 0;
        var week = (int)((utcNow - seasonStart).TotalDays / 7) + 1;
        return Math.Clamp(week, 1, 18);
    }
}
