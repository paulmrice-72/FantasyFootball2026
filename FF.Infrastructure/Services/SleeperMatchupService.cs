// FF.Infrastructure/Services/SleeperMatchupService.cs
using FF.Application.Interfaces.External;
using FF.Infrastructure.ExternalApis.Sleeper;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class SleeperMatchupService(
    ISleeperApiClient sleeperApiClient,
    ILogger<SleeperMatchupService> logger)
    : ISleeperMatchupService
{
    public async Task<IReadOnlyList<SleeperMatchupEntry>> GetMatchupsAsync(
        string leagueId, int week, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Fetching Sleeper matchups for league {LeagueId} week {Week}", leagueId, week);

        try
        {
            var matchups = await sleeperApiClient.GetMatchupsAsync(leagueId, week, ct);

            return matchups.Select(m => new SleeperMatchupEntry(
                MatchupId: m.MatchupId,
                RosterId: m.RosterId,
                Starters: m.Starters ?? [],
                Players: m.Players ?? []))
            .ToList()
            .AsReadOnly();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to fetch Sleeper matchups for league {LeagueId} week {Week}",
                leagueId, week);
            return [];
        }
    }
}