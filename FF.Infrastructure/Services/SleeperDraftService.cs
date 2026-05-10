// FF.Infrastructure/Services/SleeperDraftService.cs
using FF.Application.Interfaces.Services;
using FF.Infrastructure.ExternalApis.Sleeper;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class SleeperDraftService(
    ISleeperApiClient sleeperApiClient,
    ILogger<SleeperDraftService> logger) : ISleeperDraftService
{
    // ── Existing methods ─────────────────────────────────────────────────

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

    public async Task<SleeperDraftStatusDto> GetDraftStatusAsync(string draftId, CancellationToken ct = default)
    {
        try
        {
            var draft = await sleeperApiClient.GetDraftAsync(draftId, ct);
            var rounds = draft.Settings?.Rounds ?? 0;
            var teams = draft.Settings?.Teams ?? 0;
            return new SleeperDraftStatusDto(
                Status: draft.Status ?? "unknown",
                TotalPicks: rounds * teams);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not retrieve draft status for draft {DraftId}", draftId);
            return new SleeperDraftStatusDto(Status: "unknown", TotalPicks: 0);
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

    // ── New / fixed methods ──────────────────────────────────────────────

    /// <summary>
    /// Builds the ordered remaining pick list correctly:
    ///   1. Load draft metadata (slot_to_roster_id gives baseline ownership per slot)
    ///   2. Load per-draft traded picks to override ownership for swapped slots
    ///   3. Load made picks to know which slots are already taken
    ///   4. Generate all slots in order, subtract made, return remaining in pick order
    ///
    /// Snake vs linear: Sleeper's slot_to_roster_id is indexed 1-N for the first round.
    /// For snake, odd rounds go 1→N, even rounds go N→1 — the slot column stays fixed
    /// but the roster at that column alternates direction each round.
    /// </summary>
    public async Task<List<SleeperRemainingPickDto>> GetRemainingPicksAsync(
        string draftId,
        int myRosterId,
        Dictionary<int, SleeperTeamInfoDto> teamInfoMap,
        CancellationToken ct = default)
    {
        try
        {
            // Parallel fetch: draft metadata, traded picks for this draft, made picks
            var draftTask = sleeperApiClient.GetDraftAsync(draftId, ct);
            var tradedTask = sleeperApiClient.GetDraftTradedPicksAsync(draftId, ct);
            var madeTask = sleeperApiClient.GetDraftPicksAsync(draftId, ct);

            await Task.WhenAll(draftTask, tradedTask, madeTask);

            var draft = draftTask.Result;
            var tradedPicks = tradedTask.Result;
            var madePicks = madeTask.Result;

            int rounds = draft.Settings?.Rounds ?? 0;
            int teams = draft.Settings?.Teams ?? 0;
            bool isSnake = string.Equals(draft.Type, "snake", StringComparison.OrdinalIgnoreCase);

            if (rounds == 0 || teams == 0 || draft.SlotToRosterId is null)
            {
                logger.LogWarning("Draft {DraftId} missing settings or slot map — cannot build Up Next", draftId);
                return [];
            }

            // Build baseline: slot (1-based) → roster_id from draft metadata
            // slot_to_roster_id keys are strings ("1", "2", ...)
            var slotToRoster = draft.SlotToRosterId
                .Where(kv => int.TryParse(kv.Key, out _))
                .ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);

            // Build traded pick override: round+originalRosterId → newOwnerId
            // SleeperDraftPickDto: RosterId = original owner, OwnerId = current owner
            // Key: round|originalRosterId so we can look up overrides per round
            var tradeOverrides = new Dictionary<string, int>();
            foreach (var tp in tradedPicks)
                tradeOverrides[$"{tp.Round}|{tp.RosterId}"] = tp.OwnerId;

            // Made picks: set of absolute pick numbers already taken
            var madePickNos = madePicks
                .Where(p => !string.IsNullOrEmpty(p.PlayerId))
                .Select(p => p.PickNo)
                .ToHashSet();

            var remaining = new List<SleeperRemainingPickDto>();

            for (int round = 1; round <= rounds; round++)
            {
                // Snake: even rounds reverse the slot order
                var slots = isSnake && round % 2 == 0
                    ? Enumerable.Range(1, teams).Reverse()
                    : Enumerable.Range(1, teams);

                int slotIndex = 0;
                foreach (int slot in slots)
                {
                    slotIndex++;
                    int pickNo = (round - 1) * teams + slotIndex;

                    if (madePickNos.Contains(pickNo)) continue; // already drafted

                    // Baseline owner for this slot column
                    int baseRosterId = slotToRoster.TryGetValue(slot, out var r) ? r : 0;

                    // Apply trade override if this slot was traded in this round
                    string tradeKey = $"{round}|{baseRosterId}";
                    int currentOwnerId = tradeOverrides.TryGetValue(tradeKey, out var overrideOwner)
                        ? overrideOwner
                        : baseRosterId;

                    string teamName = teamInfoMap.TryGetValue(currentOwnerId, out var info)
                        ? info.TeamName
                        : $"Team {currentOwnerId}";

                    remaining.Add(new SleeperRemainingPickDto(
                        PickNo: pickNo,
                        Round: round,
                        Slot: slot,
                        RosterId: currentOwnerId,
                        TeamName: teamName,
                        SleeperRosterId: currentOwnerId.ToString(),
                        IsMyPick: currentOwnerId == myRosterId));
                }
            }

            return remaining.OrderBy(r => r.PickNo).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not build remaining picks for draft {DraftId}", draftId);
            return [];
        }
    }

    public async Task<Dictionary<int, SleeperTeamInfoDto>> GetTeamInfoByRosterIdAsync(
        string leagueId,
        CancellationToken ct = default)
    {
        try
        {
            var rostersTask = sleeperApiClient.GetRostersAsync(leagueId, ct);
            var usersTask = sleeperApiClient.GetUsersInLeagueAsync(leagueId, ct);
            await Task.WhenAll(rostersTask, usersTask);

            var rosters = rostersTask.Result;
            var users = usersTask.Result;

            var userMap = users
                .Where(u => u.UserId is not null)
                .ToDictionary(u => u.UserId!, u => u);

            var result = new Dictionary<int, SleeperTeamInfoDto>();
            foreach (var roster in rosters)
            {
                string ownerName = "Unknown";
                string teamName = $"Team {roster.RosterId}";

                if (roster.OwnerId is not null && userMap.TryGetValue(roster.OwnerId, out var user))
                {
                    ownerName = user.DisplayName ?? ownerName;
                    teamName = !string.IsNullOrWhiteSpace(user.Metadata?.TeamName)
                        ? user.Metadata.TeamName
                        : ownerName;
                }

                result[roster.RosterId] = new SleeperTeamInfoDto(
                    RosterId: roster.RosterId,
                    TeamName: teamName,
                    OwnerName: ownerName,
                    SleeperRosterId: roster.RosterId.ToString());
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not build team info map for league {LeagueId}", leagueId);
            return [];
        }
    }

    public async Task<List<string>> GetMyRosterPlayerIdsAsync(
        string leagueId,
        int myRosterId,
        CancellationToken ct = default)
    {
        try
        {
            var rosters = await sleeperApiClient.GetRostersAsync(leagueId, ct);
            var mine = rosters.FirstOrDefault(r => r.RosterId == myRosterId);
            return mine?.Players ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not retrieve live roster for roster {RosterId} in league {LeagueId}",
                myRosterId, leagueId);
            return [];
        }
    }
}
