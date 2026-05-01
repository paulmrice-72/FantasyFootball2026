// FF.Application/Features/Trade/Queries/GetLeagueTradeContext/GetLeagueTradeContextQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Trade.Queries.GetLeagueTradeContext;

public class GetLeagueTradeContextQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    ILeagueRepository leagueRepository,
    IDynastyValuationRepository dynastyValuationRepository,
    IPickValueRepository pickValueRepository,
    ILeagueTradeContextSleeperService sleeperService,
    ILogger<GetLeagueTradeContextQueryHandler> logger)
    : IRequestHandler<GetLeagueTradeContextQuery, LeagueTradeContextDto>
{
    public async Task<LeagueTradeContextDto> Handle(
        GetLeagueTradeContextQuery request,
        CancellationToken ct)
    {
        // ── 1. Load league entity to get DraftRounds ────────────────────
        var league = await leagueRepository.GetBySleeperIdAsync(
            request.LeagueId, request.Season, ct);
        var draftRounds = league?.DraftRounds ?? 5;

        // ── 2. Load all rosters for this league from DB ─────────────────
        var allRosters = await rosterPlayerRepository.GetByLeagueAsync(
            request.LeagueId, ct);

        if (!allRosters.Any())
        {
            logger.LogWarning("No rosters found for league {LeagueId}", request.LeagueId);
            throw new InvalidOperationException(
                "No roster data found. Sync your league first.");
        }

        // ── 3. Pull traded picks and draft slot data from Sleeper ───────
        var tradedPicks = await sleeperService.GetTradedPicksAsync(
            request.LeagueId, ct);

        // Get actual pick slot numbers for current season (e.g. 1.07).
        // This is a best-effort call — returns empty dict if draft order
        // hasn't been set yet, gracefully falls back to no slot number.
        var pickSlots = await sleeperService.GetCurrentSeasonPickSlotsAsync(
            request.LeagueId, request.Season, ct);

        // ── 4. Identify my roster ───────────────────────────────────────
        var myRoster = allRosters.FirstOrDefault(r =>
            r.SleeperUserId == request.SleeperUserId);

        if (myRoster is null)
            throw new InvalidOperationException(
                "Could not identify your roster in this league.");

        // ── 5. Build team name lookup (rosterId → teamName) ─────────────
        var teamNameLookup = allRosters
            .Where(r => r.SleeperRosterId is not null)
            .ToDictionary(r => r.SleeperRosterId!, r => r.TeamName ?? "Unknown");

        // ── 6. Bulk-load dynasty valuations ─────────────────────────────
        var allPlayerIds = allRosters
            .SelectMany(r => r.PlayerIds ?? [])
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        var valuations = await dynastyValuationRepository
            .GetBySleeperPlayerIdsAsync(allPlayerIds, ct);

        var valuationLookup = valuations
            .Where(v => v.SleeperPlayerId is not null)
            .ToDictionary(v => v.SleeperPlayerId!, v => v);

        // ── 7. Derive pick ownership ────────────────────────────────────
        var futureSeasons = new[] { request.Season, request.Season + 1 };
        var rosterIds = allRosters
            .Where(r => r.SleeperRosterId is not null)
            .Select(r => r.SleeperRosterId!)
            .ToList();

        var picksByRoster = await BuildPicksPerRosterAsync(
            rosterIds, teamNameLookup, tradedPicks, pickSlots,
            request.Season, futureSeasons, draftRounds, ct);

        // ── 8. Build each team ──────────────────────────────────────────
        var teams = new List<LeagueTeamDto>();

        foreach (var roster in allRosters)
        {
            if (roster.SleeperRosterId is null) continue;

            var players = (roster.PlayerIds ?? [])
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id =>
                {
                    valuationLookup.TryGetValue(id, out var v);
                    return new LeaguePlayerDto(
                        SleeperPlayerId: id,
                        PlayerName: v?.PlayerName ?? "Unknown",
                        Position: v?.Position ?? "?",
                        NflTeam: v?.NflTeam,
                        Age: v?.Age ?? 0,
                        TradeValue: v?.TradeValue ?? 0);
                })
                .OrderByDescending(p => p.TradeValue)
                .ToList();

            var picks = picksByRoster.TryGetValue(roster.SleeperRosterId, out var rp)
                ? rp : [];

            var totalValue = players.Sum(p => p.TradeValue) + picks.Sum(p => p.EstimatedValue);

            teams.Add(new LeagueTeamDto(
                RosterId: roster.SleeperRosterId,
                TeamName: roster.TeamName ?? "Unknown",
                OwnerSleeperUserId: roster.SleeperUserId ?? string.Empty,
                Players: players,
                Picks: picks,
                TotalTradeValue: Math.Round(totalValue, 1)));
        }

        // ── 9. League rankings ──────────────────────────────────────────
        var ranked = teams
            .OrderByDescending(t => t.TotalTradeValue)
            .Select((t, i) => new LeagueRankingDto(
                Rank: i + 1,
                TeamName: t.TeamName,
                OwnerSleeperUserId: t.OwnerSleeperUserId,
                TotalTradeValue: t.TotalTradeValue,
                IsMyTeam: t.OwnerSleeperUserId == request.SleeperUserId))
            .ToList();

        // ── 10. Split my team vs opponents ──────────────────────────────
        var myTeam = teams.First(t => t.OwnerSleeperUserId == request.SleeperUserId);
        var opponents = teams
            .Where(t => t.OwnerSleeperUserId != request.SleeperUserId)
            .OrderBy(t => t.TeamName)
            .ToList();

        logger.LogInformation(
            "League trade context built for league {LeagueId}: {TeamCount} teams, " +
            "{PlayerCount} players, {PickCount} picks, {SlotCount} pick slots resolved",
            request.LeagueId, teams.Count, allPlayerIds.Count,
            picksByRoster.Values.Sum(p => p.Count), pickSlots.Count);

        return new LeagueTradeContextDto(myTeam, opponents, ranked, draftRounds);
    }

    // ── Pick derivation ──────────────────────────────────────────────────────
    // Two-pass approach — see inline comments.
    // pickSlots: (round, rosterId) → slot number within round (e.g. 7 for 1.07)
    //            Only populated for current season when draft order is set.
    private async Task<Dictionary<string, List<LeaguePickDto>>> BuildPicksPerRosterAsync(
        List<string> rosterIds,
        Dictionary<string, string> teamNameLookup,
        List<TradedPickInfo> tradedPicks,
        Dictionary<(int Round, string RosterId), int> pickSlots,
        int currentSeason,
        int[] seasons,
        int draftRounds,
        CancellationToken ct)
    {
        var result = rosterIds.ToDictionary(id => id, _ => new List<LeaguePickDto>());

        // Track which original picks have left their original owner
        var tradedAway = new HashSet<(string, int, int)>();

        // Pass 1: add all traded picks to the current owner
        foreach (var pick in tradedPicks.Where(p => seasons.Contains(p.Season)))
        {
            if (!result.ContainsKey(pick.CurrentOwnerId)) continue;

            var originalTeam = teamNameLookup.TryGetValue(pick.OriginalOwnerId, out var otn)
                ? otn : "Unknown";
            var currentTeam = teamNameLookup.TryGetValue(pick.CurrentOwnerId, out var ctn)
                ? ctn : "Unknown";

            var pickDoc = await pickValueRepository.GetAsync(pick.Round, "Mid", pick.Season, ct);
            var estimatedValue = pickDoc?.Value ?? 0;

            // For current season picks, include slot number if known (e.g. "1.07")
            var slotLabel = BuildSlotLabel(pick.Season, pick.Round, pick.CurrentOwnerId,
                currentSeason, pickSlots);

            var description = slotLabel is not null
                ? $"{pick.Season} {RoundLabel(pick.Round)} ({slotLabel} · from {originalTeam})"
                : $"{pick.Season} {RoundLabel(pick.Round)} (from {originalTeam})";

            result[pick.CurrentOwnerId].Add(new LeaguePickDto(
                Season: pick.Season,
                Round: pick.Round,
                OriginalTeamName: originalTeam,
                CurrentTeamName: currentTeam,
                Description: description,
                EstimatedValue: estimatedValue));

            tradedAway.Add((pick.OriginalOwnerId, pick.Season, pick.Round));
        }

        // Pass 2: add own picks for rounds NOT traded away
        foreach (var season in seasons)
        {
            for (var round = 1; round <= draftRounds; round++)
            {
                foreach (var rosterId in rosterIds)
                {
                    if (tradedAway.Contains((rosterId, season, round))) continue;

                    var teamName = teamNameLookup.TryGetValue(rosterId, out var tn)
                        ? tn : "Unknown";

                    var pickDoc = await pickValueRepository.GetAsync(round, "Mid", season, ct);
                    var estimatedValue = pickDoc?.Value ?? 0;

                    var slotLabel = BuildSlotLabel(season, round, rosterId,
                        currentSeason, pickSlots);

                    var description = slotLabel is not null
                        ? $"{season} {RoundLabel(round)} ({slotLabel} · own pick)"
                        : $"{season} {RoundLabel(round)} (own pick)";

                    result[rosterId].Add(new LeaguePickDto(
                        Season: season,
                        Round: round,
                        OriginalTeamName: teamName,
                        CurrentTeamName: teamName,
                        Description: description,
                        EstimatedValue: estimatedValue));
                }
            }
        }

        foreach (var key in result.Keys)
            result[key] = result[key]
                .OrderBy(p => p.Season)
                .ThenBy(p => p.Round)
                .ToList();

        return result;
    }

    /// <summary>
    /// Returns a slot label like "1.07" if the pick slot is known for this season,
    /// or null if the draft order hasn't been set or it's a future season pick.
    /// </summary>
    private static string? BuildSlotLabel(
        int season, int round, string rosterId,
        int currentSeason,
        Dictionary<(int Round, string RosterId), int> pickSlots)
    {
        if (season != currentSeason) return null;
        if (!pickSlots.TryGetValue((round, rosterId), out var slot)) return null;
        return $"{round}.{slot:D2}";
    }

    private static string RoundLabel(int round) => round switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{round}th"
    };
}

/// <summary>
/// A pick that appears in Sleeper's /traded_picks response.
/// Sleeper field mapping:
///   roster_id  → OriginalOwnerId (team that originally owned the pick)
///   owner_id   → CurrentOwnerId  (team that owns it now)
/// </summary>
public record TradedPickInfo(
    int Season,
    int Round,
    string OriginalOwnerId,
    string CurrentOwnerId);