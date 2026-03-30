// FF.Application/QueryHandlers/GetWaiverRecommendationsQueryHandler.cs
using FF.Application.Common;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.WaiverRecommendations.Queries;

public class GetWaiverRecommendationsQueryHandler(
    IPlayerProjectionRepository projectionRepository,
    IRosterPlayerRepository rosterPlayerRepository,
    IVorpRecommendationRepository vorpRepository,
    ISimulationResultRepository simulationResultRepository,
    ICacheService cache)
    : IRequestHandler<GetWaiverRecommendationsQuery, IReadOnlyList<VorpRecommendationDocument>>
{
    private static readonly Dictionary<string, int> ReplacementSlots = new()
    {
        ["QB"] = 12,
        ["RB"] = 24,
        ["WR"] = 24,
        ["TE"] = 12
    };

    public async Task<IReadOnlyList<VorpRecommendationDocument>> Handle(
        GetWaiverRecommendationsQuery request,
        CancellationToken cancellationToken)
    {
        // ── Cache check: return persisted result if available ─────────────────
        var cacheKey = CacheKeys.VorpRecommendations(
            request.SleeperLeagueId, request.Season, request.Week,
            request.Position, request.Top);

        var cached = cache.Get<IReadOnlyList<VorpRecommendationDocument>>(cacheKey);
        if (cached is not null)
            return cached;

        // 1 — Load all projections for this week (cached separately — shared across leagues)
        var projCacheKey = CacheKeys.Projections(request.Season, request.Week);
        var allProjections = cache.Get<IReadOnlyList<PlayerProjectionDocument>>(projCacheKey);
        if (allProjections is null)
        {
            allProjections = await projectionRepository
                .GetByWeekAsync(request.Season, request.Week, cancellationToken);
            if (allProjections.Count > 0)
                cache.Set(projCacheKey, allProjections, TimeSpan.FromHours(1));
        }

        if (allProjections.Count == 0)
            return [];

        // 2 — Load all rostered player IDs in this league
        var leagueRosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);

        var rosteredPlayerIds = leagueRosters
            .SelectMany(r => r.PlayerIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 3 — Load simulation results for floor/ceiling
        var simResults = new Dictionary<string, SimulationResultDocument>();
        foreach (var proj in allProjections)
        {
            if (string.IsNullOrEmpty(proj.SleeperPlayerId)) continue;
            var sim = await simulationResultRepository
                .GetMostRecentBySleeperIdAsync(proj.SleeperPlayerId, request.Season, cancellationToken);
            if (sim is not null)
                simResults[proj.SleeperPlayerId] = sim;
        }

        // 4 — Compute replacement levels per position
        var replacementLevels = ComputeReplacementLevels(allProjections);

        // 5 — Score available (unrostered) players
        var positions = new[] { "QB", "RB", "WR", "TE" };
        var recommendations = new List<VorpRecommendationDocument>();
        var now = DateTime.UtcNow;

        foreach (var pos in positions)
        {
            var replacementLevel = replacementLevels.GetValueOrDefault(pos, 0m);

            var available = allProjections
                .Where(p => p.Position == pos
                         && !rosteredPlayerIds.Contains(p.SleeperPlayerId ?? string.Empty))
                .OrderByDescending(p => p.ProjectedPoints)
                .ToList();

            for (int i = 0; i < available.Count; i++)
            {
                var proj = available[i];
                simResults.TryGetValue(proj.SleeperPlayerId ?? string.Empty, out var sim);

                recommendations.Add(new VorpRecommendationDocument
                {
                    PlayerId = proj.SleeperPlayerId ?? proj.PlayerId,
                    PlayerName = proj.PlayerName,
                    Position = pos,
                    NflTeam = proj.NflTeam,
                    Season = request.Season,
                    Week = request.Week,
                    ProjectedPoints = proj.ProjectedPoints,
                    ReplacementLevel = replacementLevel,
                    Vorp = Math.Round(proj.ProjectedPoints - replacementLevel, 2),
                    FloorPoints = sim is not null ? sim.Floor : 0m,
                    CeilingPoints = sim is not null ? sim.Ceiling : 0m,
                    PositionRank = i + 1,
                    ComputedAt = now
                });
            }
        }

        // 6 — Assign overall VORP rank across all positions
        var ranked = recommendations
            .OrderByDescending(r => r.Vorp)
            .ToList();

        for (int i = 0; i < ranked.Count; i++)
            ranked[i].VorpRank = i + 1;

        // 7 — Persist to MongoDB
        if (ranked.Count > 0)
            await vorpRepository.UpsertBatchAsync(ranked, cancellationToken);

        // 8 — Fetch filtered result, cache, and return
        var result = await vorpRepository.GetByWeekAsync(
            request.Season, request.Week, request.Position, request.Top, cancellationToken);

        if (result.Count > 0)
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));

        return result;
    }

    private static Dictionary<string, decimal> ComputeReplacementLevels(
        IReadOnlyList<PlayerProjectionDocument> allProjections)
    {
        var levels = new Dictionary<string, decimal>();

        foreach (var (pos, slotCount) in ReplacementSlots)
        {
            var ranked = allProjections
                .Where(p => p.Position == pos)
                .OrderByDescending(p => p.ProjectedPoints)
                .ToList();

            levels[pos] = ranked.Count >= slotCount
                ? ranked[slotCount - 1].ProjectedPoints
                : ranked.LastOrDefault()?.ProjectedPoints ?? 0m;
        }

        return levels;
    }
}