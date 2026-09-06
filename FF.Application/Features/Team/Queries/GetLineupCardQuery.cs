// FF.Application/Features/Team/Queries/GetLineupCardQuery.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Services.LineupOptimizer;
using FF.Domain.Enums;
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
    List<LineupCardSlotDto> Bench,
    ProjectionProvenanceDto Provenance);

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
    bool IsLocked,
    bool HasProjection);

/// <summary>
/// FAN-121: what the numbers on this card are actually made of.
///
/// Sourced from player_projections, which stamps <see cref="FF.Domain.Enums.ProjectionBasis"/>
/// per row. simulation_results does not carry provenance yet, so the counts are
/// resolved against the projection documents for the same roster/season/week.
/// </summary>
public record ProjectionProvenanceDto(
    int Projected,
    int Unprojected,
    int Carryover,
    int? CarryoverSeason,
    int RookiePrior)
{
    /// <summary>True when anything on this card is stale or missing — drives the UI banner.</summary>
    public bool NeedsDisclosure => Unprojected > 0 || Carryover > 0 || RookiePrior > 0;

    public static readonly ProjectionProvenanceDto Empty = new(0, 0, 0, null, 0);
}

// ── Handler ───────────────────────────────────────────────────────────────────

public class GetLineupCardQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IPlayerProjectionRepository projectionRepository,
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

                // FAN-124: a missing simulation is not a zero-point forecast.
                // The solver still sorts on 0m, but the flag travels with it so
                // the card can say "—" instead of inventing a 0.0.
                var hasProjection = sim is not null && sim.Median > 0m;

                // The card sums these, so it must sum EXPECTATIONS, not medians —
                // see PlayerSlot.ProjectedMean. Falls back to Median for any legacy
                // row written before Mean was populated: a slightly low number beats
                // a zero, and a zero here would read as "not projected".
                var projectedMean = sim is null
                    ? 0m
                    : sim.Mean > 0m ? sim.Mean : sim.Median;

                return new PlayerSlot
                {
                    PlayerId = id,
                    PlayerName = fullName,
                    Position = position,
                    NflTeam = player?.NflTeam ?? sim?.NflTeam ?? string.Empty,
                    ProjectedMedian = sim?.Median ?? 0m,
                    ProjectedMean = projectedMean,
                    ProjectedFloor = sim?.Floor ?? 0m,
                    ProjectedCeiling = sim?.Ceiling ?? 0m,
                    HasProjection = hasProjection,
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
            Mode = OptimizationMode.Mean,
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
                    IsLocked: true,
                    HasProjection: slot.HasProjection);
            })
            .ToList();

        // 9 — Bench = everyone NOT in the starting lineup
        var starterIds = result.Lineup.Select(s => s.PlayerId).ToHashSet();
        var benchSlots = players
            .Where(p => !starterIds.Contains(p.PlayerId))
            .OrderBy(p => p.Position)
            .ThenByDescending(p => p.ProjectedMean)
            .Select(p =>
            {
                injuryLookup.TryGetValue(p.PlayerId, out var inj);
                return new LineupCardSlotDto(
                    SlotLabel: "BENCH",
                    SleeperPlayerId: p.PlayerId,
                    PlayerName: p.PlayerName,
                    Position: p.Position,
                    NflTeam: p.NflTeam,
                    // Same measure as the starters above — a card that mixed means
                    // and medians would make bench players look worse than they are.
                    ProjectedPoints: p.ProjectedMean,
                    BoomProbability: (double?)p.BoomProbability,
                    BustProbability: (double?)p.BustProbability,
                    InjuryDesignation: inj?.Designation,
                    IsLocked: false,
                    HasProjection: p.HasProjection);
            })
            .ToList();

        // 10 — FAN-121: provenance for the whole card
        var provenance = await BuildProvenanceAsync(
            rosterDoc.PlayerIds, request.Season, request.Week, players, cancellationToken);

        return new LineupCardDto(
            Week: request.Week,
            Season: request.Season,
            TotalProjectedPoints: result.TotalProjectedPoints,
            Starters: starterSlots,
            Bench: benchSlots,
            Provenance: provenance);
    }

    /// <summary>
    /// FAN-121 — reads the basis stamped on player_projections so the card can say
    /// where its numbers came from. Deliberately non-fatal: provenance is a
    /// disclosure, and failing to load it must never take down the lineup card.
    /// </summary>
    private async Task<ProjectionProvenanceDto> BuildProvenanceAsync(
        IReadOnlyList<string> rosterPlayerIds,
        int season,
        int week,
        IReadOnlyList<PlayerSlot> players,
        CancellationToken cancellationToken)
    {
        var unprojected = players.Count(p => !p.HasProjection);

        try
        {
            var projections = await projectionRepository.GetBySleeperIdsAsync(
                rosterPlayerIds, season, week, cancellationToken);

            var carryover = projections
                .Where(p => p.Basis == nameof(ProjectionBasis.PriorSeasonCarryover))
                .ToList();

            return new ProjectionProvenanceDto(
                Projected: players.Count - unprojected,
                Unprojected: unprojected,
                Carryover: carryover.Count,
                CarryoverSeason: carryover.Count > 0
                    ? carryover.Max(p => p.BasisSeason)
                    : null,
                RookiePrior: projections.Count(p => p.Basis == nameof(ProjectionBasis.RookieProjection)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not resolve projection provenance for season {Season} week {Week}; " +
                "card will render without the basis banner", season, week);

            return new ProjectionProvenanceDto(
                Projected: players.Count - unprojected,
                Unprojected: unprojected,
                Carryover: 0,
                CarryoverSeason: null,
                RookiePrior: 0);
        }
    }
}