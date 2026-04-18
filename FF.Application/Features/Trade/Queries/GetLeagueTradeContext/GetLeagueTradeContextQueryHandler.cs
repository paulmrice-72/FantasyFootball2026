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
            logger.LogWarning(
                "No rosters found for league {LeagueId}", request.LeagueId);
            throw new InvalidOperationException(
                "No roster data found. Sync your league first.");
        }

        // ── 3. Pull traded picks from Sleeper (live call) ───────────────
        var tradedPicks = await sleeperService.GetTradedPicksAsync(
            request.LeagueId, ct);

        // ── 4. Identify my roster ───────────────────────────────────────
        var myRoster = allRosters.FirstOrDefault(r =>
            r.SleeperUserId == request.SleeperUserId);

        if (myRoster is null)
            throw new InvalidOperationException(
                "Could not identify your roster in this league.");

        // ── 5. Build team name lookup (rosterId → teamName) ─────────────
        // RosterPlayerDocument stores TeamName from Sleeper league users sync
        var teamNameLookup = allRosters
            .Where(r => r.SleeperRosterId is not null)
            .ToDictionary(r => r.SleeperRosterId!, r => r.TeamName ?? "Unknown");

        // ── 6. Derive current pick ownership ───────────────────────────
        // For each team, original picks = rounds 1..DraftRounds for future seasons
        // Traded picks override original owner
        var futureSeasons = new[] { request.Season, request.Season + 1 };
        var picksByRoster = await BuildPicksPerRosterAsync(
            allRosters.Select(r => r.SleeperRosterId!).ToList(),
            teamNameLookup,
            tradedPicks,
            futureSeasons,
            draftRounds,
            ct);

        // ── 7. Bulk-load dynasty valuations for all players ─────────────
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

        // ── 8. Build each team ─────────────────────────────────────────
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
                        PlayerName:  v?.PlayerName ?? "Unknown",
                        Position:    v?.Position   ?? "?",
                        NflTeam:     v?.NflTeam,
                        Age:         v?.Age ?? 0,
                        TradeValue:  v?.TradeValue ?? 0);
                })
                .OrderByDescending(p => p.TradeValue)
                .ToList();

            var picks = picksByRoster.TryGetValue(roster.SleeperRosterId, out var rp)
                ? rp : [];

            var totalValue = players.Sum(p => p.TradeValue)
                           + picks.Sum(p => p.EstimatedValue);

            teams.Add(new LeagueTeamDto(
                RosterId:            roster.SleeperRosterId,
                TeamName:            roster.TeamName ?? "Unknown",
                OwnerSleeperUserId:  roster.SleeperUserId ?? string.Empty,
                Players:             players,
                Picks:               picks,
                TotalTradeValue:     Math.Round(totalValue, 1)));
        }

        // ── 9. League rankings ─────────────────────────────────────────
        var ranked = teams
            .OrderByDescending(t => t.TotalTradeValue)
            .Select((t, i) => new LeagueRankingDto(
                Rank:                i + 1,
                TeamName:            t.TeamName,
                OwnerSleeperUserId:  t.OwnerSleeperUserId,
                TotalTradeValue:     t.TotalTradeValue,
                IsMyTeam:            t.OwnerSleeperUserId == request.SleeperUserId))
            .ToList();

        // ── 10. Split my team vs opponents ─────────────────────────────
        var myTeam = teams.First(t =>
            t.OwnerSleeperUserId == request.SleeperUserId);
        var opponents = teams
            .Where(t => t.OwnerSleeperUserId != request.SleeperUserId)
            .OrderBy(t => t.TeamName)
            .ToList();

        logger.LogInformation(
            "League trade context built for league {LeagueId}: {TeamCount} teams, " +
            "{PlayerCount} players, {PickCount} picks",
            request.LeagueId, teams.Count,
            allPlayerIds.Count,
            picksByRoster.Values.Sum(p => p.Count));

        return new LeagueTradeContextDto(myTeam, opponents, ranked, draftRounds);
    }

    // ── Pick derivation ─────────────────────────────────────────────────────
    // Logic: every team originally owns picks round 1..DraftRounds for each
    // future season. Traded picks override ownership.
    private async Task<Dictionary<string, List<LeaguePickDto>>> BuildPicksPerRosterAsync(
        List<string> rosterIds,
        Dictionary<string, string> teamNameLookup,
        List<TradedPickInfo> tradedPicks,
        int[] seasons,
        int draftRounds,
        CancellationToken ct)
    {
        var result = rosterIds.ToDictionary(
            id => id,
            _ => new List<LeaguePickDto>());

        foreach (var season in seasons)
        {
            for (var round = 1; round <= draftRounds; round++)
            {
                foreach (var rosterId in rosterIds)
                {
                    // Determine if this pick has been traded
                    var traded = tradedPicks.FirstOrDefault(tp =>
                        tp.Season == season &&
                        tp.Round == round &&
                        tp.PreviousOwnerId == rosterId);

                    // Current owner: traded.CurrentOwnerId if traded, else original
                    var currentOwner = traded?.CurrentOwnerId ?? rosterId;

                    if (!result.ContainsKey(currentOwner)) continue;

                    var originalTeam = teamNameLookup.TryGetValue(rosterId, out var otn)
                        ? otn : "Unknown";
                    var currentTeam = teamNameLookup.TryGetValue(currentOwner, out var ctn)
                        ? ctn : "Unknown";

                    // Estimate pick value — use Mid tier as default
                    var tier = "Mid";
                    var pickDoc = await pickValueRepository.GetAsync(round, tier, season, ct);
                    var estimatedValue = pickDoc?.Value ?? 0;

                    var isOwn = currentOwner == rosterId;
                    var suffix = isOwn ? "" : $" (from {originalTeam})";
                    var roundLabel = round switch
                    {
                        1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{round}th"
                    };

                    result[currentOwner].Add(new LeaguePickDto(
                        Season:           season,
                        Round:            round,
                        OriginalTeamName: originalTeam,
                        CurrentTeamName:  currentTeam,
                        Description:      $"{season} {roundLabel}{suffix}",
                        EstimatedValue:   estimatedValue));
                }
            }
        }

        return result;
    }
}

/// <summary>
/// Lightweight DTO for traded picks returned from Sleeper,
/// used only within the handler. Avoids leaking Sleeper DTOs into Application.
/// </summary>
public record TradedPickInfo(
    int Season,
    int Round,
    string PreviousOwnerId,   // original owner roster_id
    string CurrentOwnerId);   // current owner roster_id
