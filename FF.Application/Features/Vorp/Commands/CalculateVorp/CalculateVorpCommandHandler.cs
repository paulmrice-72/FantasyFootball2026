// FF.Application/Features/Vorp/Commands/CalculateVorp/CalculateVorpCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Vorp.Commands.CalculateVorp;

/// <summary>
/// Orchestration for L3. All the arithmetic lives in <see cref="ReplacementLevelService"/>,
/// which is pure; this class only gathers inputs and persists outputs.
/// </summary>
public class CalculateVorpCommandHandler(
    ILeagueRepository leagueRepository,
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerProjectionRepository projectionRepository,
    ISimulationResultRepository simulationRepository,
    IVorpRecommendationRepository vorpRepository,
    ILogger<CalculateVorpCommandHandler> logger)
    : IRequestHandler<CalculateVorpCommand, CalculateVorpResult>
{
    public async Task<CalculateVorpResult> Handle(
        CalculateVorpCommand request,
        CancellationToken ct)
    {
        // 1 — League: roster configuration and scoring settings both come from here.
        var league = await leagueRepository
            .GetBySleeperIdAsync(request.SleeperLeagueId, request.Season, ct);

        if (league is null)
        {
            var active = await leagueRepository.GetActiveLeaguesAsync(ct);
            league = active.FirstOrDefault(l => l.SleeperLeagueId == request.SleeperLeagueId);
        }

        if (league is null)
            throw new InvalidOperationException(
                $"League '{request.SleeperLeagueId}' not found for season {request.Season}. " +
                "VORP cannot be computed without a roster configuration.");

        var rosterConfig    = league.GetRosterConfiguration() ?? RosterConfiguration.Standard;
        var scoringSettings = league.GetScoringSettings();

        // 2 — Rosters: give us both the league size and the rostered set.
        var rosters = await rosterPlayerRepository.GetByLeagueAsync(request.SleeperLeagueId, ct);
        var teamCount = rosters.Count;

        if (teamCount == 0)
            throw new InvalidOperationException(
                $"League '{request.SleeperLeagueId}' has no rosters imported. Replacement level " +
                "is defined by league size, so there is nothing to compute against.");

        var rosteredIds = rosters
            .SelectMany(r => r.PlayerIds)
            .ToHashSet(StringComparer.Ordinal);

        // 3 — Projections for the week.
        var projections = await projectionRepository.GetByWeekAsync(request.Season, request.Week, ct);

        if (projections.Count == 0)
            return Empty(request, teamCount,
                $"No projections exist for season {request.Season} week {request.Week}.");

        // 4 — Score each stat line in THIS league's format. This is the step that
        //     makes VORP format-aware rather than half-PPR-shaped (FAN-97).
        var legacyFallbacks = 0;
        var candidates = new List<ReplacementCandidate>(projections.Count);
        var scored = new Dictionary<string, (PlayerProjectionDocument Doc, decimal Points)>(StringComparer.Ordinal);

        foreach (var p in projections)
        {
            if (string.IsNullOrEmpty(p.SleeperPlayerId)) continue;

            decimal points;
            if (p.StatLine is not null)
            {
                points = FantasyScoringService.Score(p.StatLine, scoringSettings, p.Position);
            }
            else
            {
                // Pre-Epic-20 document: stat line was never written, so the cached
                // half-PPR column is all there is. Counted and reported rather than
                // silently mixed in, because it is NOT this league's format.
                points = p.ProjectedPointsHalfPpr;
                legacyFallbacks++;
            }

            candidates.Add(new ReplacementCandidate(
                PlayerId:        p.SleeperPlayerId,
                Position:        p.Position,
                ProjectedPoints: points,
                IsRostered:      rosteredIds.Contains(p.SleeperPlayerId)));

            scored[p.SleeperPlayerId] = (p, points);
        }

        // 5 — The actual L3 computation.
        var levels = ReplacementLevelService.Compute(candidates, rosterConfig, teamCount);

        // 6 — Distribution context, where a simulation exists. Absent is left null.
        var sims = await simulationRepository.GetByWeekAsync(request.Season, request.Week, ct);
        var simLookup = sims
            .Where(s => !string.IsNullOrEmpty(s.SleeperPlayerId))
            .GroupBy(s => s.SleeperPlayerId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CalculatedAt).First(),
                          StringComparer.Ordinal);

        var missingDistribution = 0;
        var now = DateTime.UtcNow;

        var docs = new List<VorpRecommendationDocument>(candidates.Count);

        foreach (var c in candidates)
        {
            if (!scored.TryGetValue(c.PlayerId, out var s)) continue;
            levels.TryGetValue(c.Position, out var level);

            simLookup.TryGetValue(c.PlayerId, out var sim);
            if (sim is null) missingDistribution++;

            docs.Add(new VorpRecommendationDocument
            {
                SleeperLeagueId           = request.SleeperLeagueId,
                PlayerId                  = c.PlayerId,
                PlayerName                = s.Doc.PlayerName,
                Position                  = c.Position,
                NflTeam                   = s.Doc.NflTeam,
                Season                    = request.Season,
                Week                      = request.Week,
                IsRostered                = c.IsRostered,
                ProjectedPoints           = c.ProjectedPoints,
                FloorPoints               = sim?.Floor,
                CeilingPoints             = sim?.Ceiling,
                ReplacementLevel          = level?.StructuralLevel ?? 0m,
                Vorp                      = ReplacementLevelService.StructuralVorp(c, levels),
                ReplacementLevelFreeAgent = level?.FreeAgentBest,
                VorpFreeAgent             = ReplacementLevelService.FreeAgentVorp(c, levels),
                ReplacementPoolExhausted  = level?.PoolExhausted ?? false,
                ComputedAt                = now
            });
        }

        // 7 — Ranks. Overall by VORP, and within position.
        var overallRank = 1;
        foreach (var d in docs.OrderByDescending(d => d.Vorp))
            d.VorpRank = overallRank++;

        foreach (var group in docs.GroupBy(d => d.Position, StringComparer.OrdinalIgnoreCase))
        {
            var posRank = 1;
            foreach (var d in group.OrderByDescending(d => d.Vorp))
                d.PositionRank = posRank++;
        }

        // 8 — Replace the week wholesale, so a player who dropped out of the
        //     projection set does not linger with a stale VORP from a previous run.
        await vorpRepository.DeleteForWeekAsync(
            request.SleeperLeagueId, request.Season, request.Week, ct);
        await vorpRepository.UpsertBatchAsync(docs, ct);

        var exhausted = levels.Values
            .Where(l => l.PoolExhausted)
            .Select(l => l.Position)
            .ToList();

        logger.LogInformation(
            "VORP computed for league {League} S{Season}W{Week}: {Count} players, {Teams} teams, " +
            "{Legacy} legacy fallbacks, {NoSim} without a distribution",
            request.SleeperLeagueId, request.Season, request.Week,
            docs.Count, teamCount, legacyFallbacks, missingDistribution);

        return new CalculateVorpResult(
            SleeperLeagueId:  request.SleeperLeagueId,
            Season:           request.Season,
            Week:             request.Week,
            TeamCount:        teamCount,
            PlayersScored:    docs.Count,
            RosteredPlayers:  candidates.Count(c => c.IsRostered),
            FreeAgents:       candidates.Count(c => !c.IsRostered),
            LegacyPointsOnlyFallbacks: legacyFallbacks,
            MissingDistribution:       missingDistribution,
            StructuralReplacementByPosition:
                levels.ToDictionary(kv => kv.Key, kv => kv.Value.StructuralLevel),
            FreeAgentReplacementByPosition:
                levels.ToDictionary(kv => kv.Key, kv => kv.Value.FreeAgentBest),
            PositionsWithExhaustedPool: exhausted,
            Warning: exhausted.Count > 0
                ? $"Projection pool too shallow at {string.Join(", ", exhausted)} — " +
                  "replacement level fell back to the last available projection."
                : null);
    }

    private static CalculateVorpResult Empty(
        CalculateVorpCommand request, int teamCount, string warning) =>
        new(request.SleeperLeagueId, request.Season, request.Week, teamCount,
            0, 0, 0, 0, 0,
            new Dictionary<string, decimal>(),
            new Dictionary<string, decimal?>(),
            [],
            warning);
}
