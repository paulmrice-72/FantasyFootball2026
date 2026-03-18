using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

public class DefensiveRankingService(
    IPlayerGameLogRepository gameLogRepository,
    IDefensiveRankingRepository defensiveRankingRepository,
    ILogger<DefensiveRankingService> logger) : IDefensiveRankingService
{
    private static readonly string[] TrackedPositions = ["QB", "RB", "WR", "TE"];

    public async Task CalculateAsync(int season, int throughWeek, CancellationToken ct = default)
    {
        logger.LogInformation(
            "DefensiveRankingService starting for season {Season} through week {Week}",
            season, throughWeek);

        var logs = await gameLogRepository.GetBySeasonAsync(season, ct);
        var regLogs = logs
            .Where(x => x.SeasonType == "REG"
                     && x.Week >= 1
                     && x.Week <= throughWeek
                     && !string.IsNullOrEmpty(x.OpponentTeam)
                     && !string.IsNullOrEmpty(x.Position)
                     && TrackedPositions.Contains(x.Position))
            .ToList();

        if (regLogs.Count == 0)
        {
            logger.LogWarning("No game logs found for season {Season} through week {Week}",
                season, throughWeek);
            return;
        }

        var documents = new List<DefensiveRankingDocument>();
        var l4wMinWeek = Math.Max(1, throughWeek - 3);

        // ── SOS Pre-computation ───────────────────────────────────────────────
        // For each (offensiveTeam, position) pair, compute average PPR points
        // scored per game. This measures offensive strength — used to weight
        // how difficult each defense's schedule actually was.
        //
        // Key: (NflTeam, Position)  Value: avg PPR points scored per game
        var offensiveStrength = regLogs
            .GroupBy(x => (Team: x.NflTeam, x.Position))
            .ToDictionary(
                g => g.Key,
                g => g.Average(x => (double)(x.FantasyPointsPpr.GetValueOrDefault() > 0
                    ? x.FantasyPointsPpr.GetValueOrDefault()
                    : x.FantasyPoints.GetValueOrDefault())));

        foreach (var position in TrackedPositions)
        {
            var posLogs = regLogs.Where(x => x.Position == position).ToList();

            // League-wide average offensive output for this position
            // Used as the neutral baseline for the SOS factor
            var leagueAvgOffense = offensiveStrength
                .Where(kv => kv.Key.Position == position)
                .Select(kv => kv.Value)
                .DefaultIfEmpty(1.0)
                .Average();

            var seasonGroups = posLogs
                .GroupBy(x => x.OpponentTeam)
                .Select(g => new
                {
                    Team = g.Key,
                    AvgAllowed = g.Average(x => (double)(x.FantasyPointsPpr.GetValueOrDefault() > 0
                        ? x.FantasyPointsPpr.GetValueOrDefault()
                        : x.FantasyPoints.GetValueOrDefault())),
                    GamesAllowed = g.Select(x => x.Week).Distinct().Count(),
                    // Collect the offensive teams this defense faced
                    OpponentTeams = g.Select(x => x.NflTeam).Distinct().ToList()
                })
                .ToList();

            var l4wGroups = posLogs
                .Where(x => x.Week >= l4wMinWeek)
                .GroupBy(x => x.OpponentTeam)
                .Select(g => new
                {
                    Team = g.Key,
                    AvgAllowedL4W = g.Average(x => (double)(x.FantasyPointsPpr.GetValueOrDefault() > 0
                        ? x.FantasyPointsPpr.GetValueOrDefault()
                        : x.FantasyPoints.GetValueOrDefault()))
                })
                .ToDictionary(x => x.Team ?? string.Empty, x => x.AvgAllowedL4W);

            var seasonRanked = seasonGroups.OrderBy(x => x.AvgAllowed).ToList();
            var teamCount = seasonRanked.Count;

            for (var index = 0; index < teamCount; index++)
            {
                var entry = seasonRanked[index];

                var seasonPct = teamCount > 1
                    ? Math.Round((decimal)index / (teamCount - 1) * 100, 1)
                    : 50m;

                l4wGroups.TryGetValue(entry.Team ?? string.Empty, out var avgL4W);

                var l4wRanked = l4wGroups.OrderBy(x => x.Value).ToList();
                var l4wIndex = l4wRanked.FindIndex(x => x.Key == (entry.Team ?? string.Empty));
                var l4wPct = l4wIndex >= 0 && l4wRanked.Count > 1
                    ? Math.Round((decimal)l4wIndex / (l4wRanked.Count - 1) * 100, 1)
                    : seasonPct;

                var difficultyScore = Math.Round(seasonPct * 0.4m + l4wPct * 0.6m, 1);

                // ── SOS Adjustment ────────────────────────────────────────────
                // Average offensive strength of opponents this defense faced.
                // If they faced mostly weak offenses, factor < 1 → score deflated.
                // If they faced mostly strong offenses, factor > 1 → score bumped.
                var opponentStrengthValues = entry.OpponentTeams
                    .Select(t => offensiveStrength.TryGetValue((t, position), out var s) ? s : leagueAvgOffense)
                    .ToList();

                var avgOpponentStrength = opponentStrengthValues.Count > 0
                    ? opponentStrengthValues.Average()
                    : leagueAvgOffense;

                var sosFactor = leagueAvgOffense > 0
                    ? avgOpponentStrength / leagueAvgOffense
                    : 1.0;

                // Apply factor and clamp to 0–100
                var sosAdjustedScore = Math.Round(
                    Math.Clamp((double)difficultyScore * sosFactor, 0.0, 100.0), 1);

                documents.Add(new DefensiveRankingDocument
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Team = entry.Team ?? string.Empty,
                    Position = position,
                    Season = season,
                    Week = throughWeek,
                    AvgFantasyPointsAllowed = Math.Round((decimal)entry.AvgAllowed, 2),
                    AvgFantasyPointsAllowedL4W = Math.Round((decimal)avgL4W, 2),
                    SeasonPercentile = seasonPct,
                    L4WPercentile = l4wPct,
                    DifficultyScore = difficultyScore,
                    SosAdjustedDifficultyScore = (decimal)sosAdjustedScore,
                    GamesAllowed = entry.GamesAllowed,
                    CalculatedAt = DateTime.UtcNow
                });
            }
        }

        logger.LogInformation(
            "Calculated {Count} defensive ranking documents for season {Season} week {Week}",
            documents.Count, season, throughWeek);

        await defensiveRankingRepository.UpsertBatchAsync(documents, ct);

        logger.LogInformation("DefensiveRankingService complete");
    }
}