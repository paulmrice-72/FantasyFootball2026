// FF.Infrastructure/Services/SleeperDraftService.cs
using FF.Application.Interfaces.Services;
using FF.Infrastructure.ExternalApis.Sleeper;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class SleeperDraftService(
    ISleeperApiClient sleeperApiClient,
    ILogger<SleeperDraftService> logger) : ISleeperDraftService
{
    public async Task<string?> GetActiveDraftIdAsync(string leagueId, CancellationToken ct = default)
    {
        try
        {
            var drafts = await sleeperApiClient.GetDraftsForLeagueAsync(leagueId, ct);
            var active = drafts
                .Where(d => d.Status != "complete")
                .OrderByDescending(d => d.Season)
                .FirstOrDefault();

            if (active is not null)
                logger.LogInformation(
                    "Found active Sleeper draft {DraftId} (status: {Status}) for league {League}",
                    active.DraftId, active.Status, leagueId);

            return active?.DraftId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not retrieve drafts for league {League}", leagueId);
            return null;
        }
    }

    public async Task<int?> GetMyRosterIdAsync(string leagueId, string sleeperUserId, CancellationToken ct = default)
    {
        try
        {
            var rosters = await sleeperApiClient.GetRostersAsync(leagueId, ct);
            var mine = rosters.FirstOrDefault(r => r.OwnerId == sleeperUserId);
            return mine?.RosterId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not retrieve roster_id for user {UserId} in league {League}",
                sleeperUserId, leagueId);
            return null;
        }
    }

    public async Task<List<SleeperMadePickDto>> GetMadePicksAsync(string draftId, CancellationToken ct = default)
    {
        var picks = await sleeperApiClient.GetDraftPicksAsync(draftId, ct);

        return picks
            .Where(p => !string.IsNullOrEmpty(p.PlayerId))
            .Select(p => new SleeperMadePickDto(
                PlayerId: p.PlayerId!,
                PlayerName: p.Metadata is not null
                    ? $"{p.Metadata.FirstName} {p.Metadata.LastName}".Trim()
                    : p.PlayerId!,
                Position: p.Metadata?.Position ?? "UNK",
                Round: p.Round,
                DraftSlot: p.DraftSlot,
                RosterId: p.RosterId?.ToString() ?? string.Empty
            ))  
            .ToList();
    }
}