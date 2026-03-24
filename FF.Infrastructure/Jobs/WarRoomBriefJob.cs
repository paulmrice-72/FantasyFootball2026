// FF.Infrastructure/Jobs/WarRoomBriefJob.cs
using FF.Application.Services;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Generates War Room Briefs for all users with active league memberships.
/// Fires Sunday 8am UTC (3am ET) — before the main slate.
/// </summary>
public class WarRoomBriefJob(
    WarRoomBriefService briefService,
    FFDbContext dbContext,
    ILogger<WarRoomBriefJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var season = GetCurrentNflSeason();
        var week = GetCurrentNflWeek();

        logger.LogInformation(
            "WarRoomBriefJob starting — Season {Season} Week {Week}", season, week);

        // Get all users with active league memberships this season
        var userIds = await dbContext.LeagueMemberships
            .Where(m => m.Season == season && m.IsActive)
            .Select(m => new { m.UserId, m.SleeperUserId })
            .Distinct()
            .ToListAsync(ct);

        // Get user emails from AspNetUsers
        var users = await dbContext.Users
            .Where(u => userIds.Select(x => x.UserId).Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        var generated = 0;
        var failed = 0;

        foreach (var user in users)
        {
            try
            {
                await briefService.GenerateBriefAsync(
                    user.Id,
                    user.Email ?? string.Empty,
                    season,
                    week,
                    ct);
                generated++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to generate brief for user {UserId}", user.Id);
                failed++;
            }
        }

        logger.LogInformation(
            "WarRoomBriefJob complete — {Generated} generated, {Failed} failed",
            generated, failed);
    }

    private static int GetCurrentNflSeason()
    {
        var now = DateTime.UtcNow;
        return now.Month >= 3 ? now.Year : now.Year - 1;
    }

    private static int GetCurrentNflWeek()
    {
        var now = DateTime.UtcNow;
        var season = GetCurrentNflSeason();
        var sept1 = new DateTime(season, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)sept1.DayOfWeek + 7) % 7;
        var seasonStart = sept1.AddDays(daysUntilThursday);
        if (now < seasonStart) return 18;
        var week = (int)((now - seasonStart).TotalDays / 7) + 1;
        return Math.Clamp(week, 1, 18);
    }
}