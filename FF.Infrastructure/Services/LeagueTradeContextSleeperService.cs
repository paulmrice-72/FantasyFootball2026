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
                    Season: int.TryParse(p.Season, out var s) ? s : 0,
                    Round: p.Round,
                    OriginalOwnerId: p.RosterId.ToString(),
                    CurrentOwnerId: p.OwnerId.ToString()))
                .Where(p => p.Season > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to load traded picks from Sleeper for league {LeagueId}", leagueId);
            return [];
        }
    }

    /// <summary>
    /// Fetches pick slot numbers for the current season's rookie draft.
    /// Returns a lookup of (round, rosterId) → pick slot number within round
    /// (e.g. round=1, rosterId="5" → 7 means pick 1.07).
    /// Returns empty dict if draft order hasn't been set yet or on any error.
    /// </summary>
    public async Task<Dictionary<(int Round, string RosterId), int>> GetCurrentSeasonPickSlotsAsync(
        string leagueId,
        int season,
        CancellationToken ct = default)
    {
        try
        {
            // Get all drafts for the league — most recent first
            var drafts = await sleeperApiClient.GetDraftsForLeagueAsync(leagueId, ct);

            // Find the rookie/annual draft for this season (not the startup draft)
            // Dynasty leagues have a startup draft (type=snake, large rounds) and
            // annual rookie drafts (fewer rounds). Find the one for this season.
            var rookieDraft = drafts
                .Where(d => d.Season == season.ToString() && d.Status != "complete")
                .OrderByDescending(d => d.DraftId) // most recent first
                .FirstOrDefault()
                // Fallback: most recent complete draft for this season
                ?? drafts
                    .Where(d => d.Season == season.ToString())
                    .OrderByDescending(d => d.DraftId)
                    .FirstOrDefault();

            if (rookieDraft?.DraftId is null)
            {
                logger.LogInformation(
                    "No draft found for league {LeagueId} season {Season}", leagueId, season);
                return [];
            }

            // If SlotToRosterId isn't set, draft order hasn't been randomized yet
            if (rookieDraft.SlotToRosterId is null || rookieDraft.SlotToRosterId.Count == 0)
            {
                logger.LogInformation(
                    "Draft order not yet set for draft {DraftId}", rookieDraft.DraftId);
                return [];
            }

            // Get all picks for this draft
            var picks = await sleeperApiClient.GetDraftPicksAsync(rookieDraft.DraftId, ct);

            // Build lookup: (round, rosterId) → slot within round
            // draft_slot is the column (1-based position in the round)
            // We want the slot number within the round, e.g. 1.07 = round 1, slot 7
            var lookup = new Dictionary<(int, string), int>();

            foreach (var pick in picks.Where(p => p.RosterId is not null))
            {
                var key = (pick.Round, pick.RosterId!);
                // draft_slot gives us the column position within the round (1.01, 1.02, etc.)
                if (!lookup.ContainsKey(key))
                    lookup[key] = pick.DraftSlot;
            }

            logger.LogInformation(
                "Loaded {Count} pick slots for draft {DraftId}", lookup.Count, rookieDraft.DraftId);

            return lookup;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to load draft pick slots for league {LeagueId} season {Season}",
                leagueId, season);
            return [];
        }
    }
}