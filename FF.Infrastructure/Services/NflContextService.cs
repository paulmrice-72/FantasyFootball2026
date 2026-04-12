using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;

namespace FF.Infrastructure.Services;

/// <summary>
/// Resolves current NFL season and week.
/// Checks admin simulation override first; falls back to calendar calculation.
/// </summary>
public class NflContextService(IAppSettingsRepository appSettingsRepo) : INflContextService
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

        var season = CalcSeason(DateTime.UtcNow);
        return CalcWeek(DateTime.UtcNow, season);
    }

    public async Task<(int Season, int Week)> GetContextAsync()
    {
        var settings = await appSettingsRepo.GetAsync();
        var now = DateTime.UtcNow;

        var season = settings.SimulationSeasonOverride ?? CalcSeason(now);
        var week = settings.SimulationWeekOverride ?? CalcWeek(now, season);

        return (season, week);
    }

    // ── Calendar fallback logic ───────────────────────────────────────────
    public static int CalcSeason(DateTime utcNow) =>
        utcNow.Month >= 3 ? utcNow.Year : utcNow.Year - 1;

    public static int CalcWeek(DateTime utcNow, int season)
    {
        var sept1 = new DateTime(season, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)sept1.DayOfWeek + 7) % 7;
        var seasonStart = sept1.AddDays(daysUntilThursday);
        if (utcNow < seasonStart) return 18;
        var week = (int)((utcNow - seasonStart).TotalDays / 7) + 1;
        return Math.Clamp(week, 1, 18);
    }
}