// FF.Infrastructure/Services/LeagueTradeContextSleeperService.cs
using FF.Application.Features.Trade.Queries.GetLeagueTradeContext;
using FF.Application.Interfaces.Services;
using FF.Infrastructure.ExternalApis.Sleeper;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class LeagueTradeContextSleeperService(
    ISleeperApiClient sleeperApiClient,
    ILogger<LeagueTradeContextSleeperService> logger)
    : ILeagueTradeContextSleeperService
{
    public async Task<List<TradedPickInfo>> GetTradedPicksAsync(
        string leagueId,
        CancellationToken ct = default)
    {
        try
        {
            var picks = await sleeperApiClient.GetTradedPicksAsync(leagueId, ct);

            return picks
                .Select(p => new TradedPickInfo(
                    Season:          int.TryParse(p.Season, out var s) ? s : 0,
                    Round:           p.Round,
                    PreviousOwnerId: p.PreviousOwnerId.ToString(),
                    CurrentOwnerId:  p.OwnerId.ToString()))
                .Where(p => p.Season > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            // Non-fatal — if Sleeper is unavailable, return empty list.
            // Picks section will show "unable to load" in UI.
            logger.LogWarning(ex,
                "Failed to load traded picks from Sleeper for league {LeagueId}",
                leagueId);
            return [];
        }
    }
}
