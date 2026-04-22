// FF.Infrastructure/Jobs/WarRoomBriefJob.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Generates War Room Briefs for all users with active league memberships.
/// Fires Sunday 8am UTC (3am ET) — before the main slate.
/// Season/week sourced from INflContextService — respects admin override.
/// </summary>
public class WarRoomBriefJob(
    WarRoomBriefService briefService,
    FFDbContext dbContext,
    IPlatformSettingsRepository platformSettingsRepo,
    INflContextService nflContextService,
    ILogger<WarRoomBriefJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default, bool forceRun = false)
    {
        if (!forceRun)
        {
            var settings = await platformSettingsRepo.GetAsync();
            if (!settings.AiJobsEnabled)
            {
                logger.LogInformation("WarRoomBriefJob skipped — AiJobsEnabled is false");
                return;
            }
        }

        var (season, week) = await nflContextService.GetContextAsync();

        logger.LogInformation(
            "WarRoomBriefJob starting — Season {Season} Week {Week}",
            season, week);

        var userIds = await dbContext.LeagueMemberships
            .Where(m => m.Season == season && m.IsActive)
            .Select(m => new { m.UserId, m.SleeperUserId })
            .Distinct()
            .ToListAsync(ct);

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
                logger.LogError(ex, "Failed to generate brief for user {UserId}", user.Id);
                failed++;
            }
        }

        logger.LogInformation(
            "WarRoomBriefJob complete — {Generated} generated, {Failed} failed",
            generated, failed);
    }
}