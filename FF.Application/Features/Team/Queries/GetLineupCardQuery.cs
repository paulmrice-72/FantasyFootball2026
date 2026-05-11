// FF.Application/Features/Team/Queries/GetLineupCardQuery.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Services.LineupOptimizer;
using FF.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

public record GetLineupCardQuery(
    string SleeperUserId,
    string SleeperLeagueId,
    int Season,
    int Week) : IRequest<LineupCardDto?>;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record LineupCardDto(
    int Week,
    int Season,
    decimal TotalProjectedPoints,
    List<LineupCardSlotDto> Starters,
    List<LineupCardSlotDto> Bench);

public record LineupCardSlotDto(
    string SlotLabel,
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string NflTeam,
    decimal ProjectedPoints,
    double? BoomProbability,
    double? BustProbability,
    string? InjuryDesignation,
    bool IsLocked);

// ── Handler ───────────────────────────────────────────────────────────────────

public class GetLineupCardQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IInjuryAlertRepository injuryAlertRepository,
    ILeagueRepository leagueRepository,
    ILogger<GetLineupCardQueryHandler> logger)
    : IRequestHandler<GetLineupCardQuery, LineupCardDto?>
{
    public async Task<LineupCardDto?> Handle(
        GetLineupCardQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building Lineup Card for user {UserId} league {LeagueId} week {Week}",
            request.SleeperUserId, request.SleeperLeagueId, request.Week);

        // 1 — Load roster
        var rosterDoc = await rosterPlayerRepository.GetBySleeperUserIdAsync(
            request.SleeperUserId, request.SleeperLeagueId, cancellationToken);
        if (rosterDoc is null || rosterDoc.PlayerIds.Count == 0)
            return null;

        // 2 — Load league config
        var league = await leagueRepository
            .GetBySleeperIdAsync(request.SleeperLeagueId, request.Season, cancellationToken);

        if (league is null)
        {
            var activeLeagues = await leagueRepository.GetActiveLeaguesAsync(cancellationToken);
            league = activeLeagues.FirstOrDefault(l =>
                l.SleeperLeagueId == request.SleeperLeagueId);
        }

        var rosterConfig = league?.GetRosterConfiguration()
            ?? RosterConfiguration.Standard;

        // 3 — Load sim results for this week, filtered to this roster
        var allSims = await simulationRepository.GetByWeekAsync(
            request.Season, request.Week, cancellationToken);
        var simLookup = allSims
            .Where(s => rosterDoc.PlayerIds.Contains(s.SleeperPlayerId ?? string.Empty))
            .GroupBy(s => s.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CalculatedAt).First());

        // 4 — Load full player records for accurate FullName
        //     SimulationResultDocument.PlayerName can be abbreviated (e.g. "J.Fields").
        //     IPlayerRepository.GetBySleeperIdsAsync returns the canonical FullName.
        var playerRecords = await playerRepository.GetBySleeperIdsAsync(
            rosterDoc.PlayerIds, cancellationToken);
        var playerLookup = playerRecords
            .Where(p => p.SleeperPlayerId is not null)
            .GroupBy(p => p.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.First());

        // 5 — Load injury alerts
        var injuries = await injuryAlertRepository.GetActiveAlertsAsync(null, cancellationToken);
        var injuryLookup = injuries
            .Where(i => i.SleeperPlayerId != null)
            .GroupBy(i => i.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.First());

        // 6 — Build PlayerSlot list for the optimizer
        var players = rosterDoc.PlayerIds
            .Where(id =>
            {
                injuryLookup.TryGetValue(id, out var inj);
                return inj?.Designation is not ("IR" or "Out");
            })
            .Select(id =>
            {
                simLookup.TryGetValue(id, out var sim);
                playerLookup.TryGetValue(id, out var player);
                injuryLookup.TryGetValue(id, out var inj);

                // Prefer full name from player record; fall back to sim name
                var fullName = player?.FullName
                    ?? sim?.PlayerName
                    ?? "Unknown";

                var position = player?.Position.ToString()
                    ?? sim?.Position
                    ?? "?";

                return new PlayerSlot
                {
                    PlayerId = id,
                    PlayerName = fullName,
                    Position = position,
                    NflTeam = player?.NflTeam ?? sim?.NflTeam ?? string.Empty,
                    ProjectedMedian = sim?.Median ?? 0m,
                    ProjectedFloor = sim?.Floor ?? 0m,
                    ProjectedCeiling = sim?.Ceiling ?? 0m,
                    BoomProbability = sim?.BoomProbability,
                    BustProbability = sim?.BustProbability,
                    IsLocked = false,
                    IsExcluded = false
                };
            })
            .Where(p => p.Position != "?")
            .ToList();

        if (players.Count == 0)
        {
            logger.LogWarning(
                "No eligible players with sim data found for lineup card — user {UserId} league {LeagueId} week {Week}",
                request.SleeperUserId, request.SleeperLeagueId, request.Week);
            return null;
        }

        // 7 — Run the optimizer
        var optimizerInput = new LineupOptimizerInput
        {
            AvailablePlayers = players,
            RosterConfig = rosterConfig,
            Mode = OptimizationMode.Median,
            LockedPlayerIds = [],
            ExcludedPlayerIds = []
        };

        var result = LineupOptimizerService.Optimize(optimizerInput);

        if (!result.Success)
        {
            logger.LogWarning("Lineup card optimizer failed: {Error}", result.ErrorMessage);
            return null;
        }

        // 8 — Build slot labels (RB1/RB2, WR1/WR2/WR3, etc.)
        var slotCounters = new Dictionary<string, int>();
        var starterSlots = result.Lineup
            .Select(slot =>
            {
                slotCounters.TryGetValue(slot.SlotType, out var count);
                slotCounters[slot.SlotType] = count + 1;

                var label = slot.SlotType is "FLEX" or "SUPERFLEX" or "QB" or "TE"
                    ? slot.SlotType
                    : $"{slot.SlotType}{count + 1}";

                injuryLookup.TryGetValue(slot.PlayerId, out var inj);
                var p = players.FirstOrDefault(x => x.PlayerId == slot.PlayerId);

                return new LineupCardSlotDto(
                    SlotLabel: label,
                    SleeperPlayerId: slot.PlayerId,
                    PlayerName: slot.PlayerName,   // full name from player record above
                    Position: slot.Position,
                    NflTeam: p?.NflTeam ?? string.Empty,
                    ProjectedPoints: slot.ProjectedPoints,
                    BoomProbability: (double?)p?.BoomProbability,
                    BustProbability: (double?)p?.BustProbability,
                    InjuryDesignation: inj?.Designation,
                    IsLocked: true);
            })
            .ToList();

        // 9 — Bench = everyone NOT in the starting lineup
        var starterIds = result.Lineup.Select(s => s.PlayerId).ToHashSet();
        var benchSlots = players
            .Where(p => !starterIds.Contains(p.PlayerId))
            .OrderBy(p => p.Position)
            .ThenByDescending(p => p.ProjectedMedian)
            .Select(p =>
            {
                injuryLookup.TryGetValue(p.PlayerId, out var inj);
                return new LineupCardSlotDto(
                    SlotLabel: "BENCH",
                    SleeperPlayerId: p.PlayerId,
                    PlayerName: p.PlayerName,
                    Position: p.Position,
                    NflTeam: p.NflTeam,
                    ProjectedPoints: p.ProjectedMedian,
                    BoomProbability: (double?)p.BoomProbability,
                    BustProbability: (double?)p.BustProbability,
                    InjuryDesignation: inj?.Designation,
                    IsLocked: false);
            })
            .ToList();

        return new LineupCardDto(
            Week: request.Week,
            Season: request.Season,
            TotalProjectedPoints: result.TotalProjectedPoints,
            Starters: starterSlots,
            Bench: benchSlots);
    }
}