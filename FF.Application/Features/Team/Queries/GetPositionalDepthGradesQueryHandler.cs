// FF.Application/Features/Team/Queries/GetPositionalDepthGradesQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

public class GetPositionalDepthGradesQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IInjuryAlertRepository injuryAlertRepository,
    ILogger<GetPositionalDepthGradesQueryHandler> logger)
    : IRequestHandler<GetPositionalDepthGradesQuery, PositionalDepthGradesDto?>
{
    private static readonly double[] DepthWeights = [1.0, 0.60, 0.35, 0.20, 0.10];

    private static readonly (double Min, string Grade, string Label)[] GradeTable =
    [
        (90, "A+", "Elite"),
        (78, "A",  "Excellent"),
        (65, "B+", "Strong"),
        (52, "B",  "Solid"),
        (40, "C+", "Average"),
        (30, "C",  "Below Average"),
        (20, "D",  "Weak"),
        (0,  "F",  "Dire")
    ];

    private static readonly Dictionary<string, double> PositionBaseline = new()
    {
        ["QB"] = 19.3,
        ["RB"] = 15.1,
        ["WR"] = 13.1,
        ["TE"] = 12.1
    };

    private static readonly Dictionary<string, int> StarterSlots = new()
    {
        ["QB"] = 1,
        ["RB"] = 2,
        ["WR"] = 3,
        ["TE"] = 1
    };

    private const double FillerFloorFraction = 0.50;

    public async Task<PositionalDepthGradesDto?> Handle(
        GetPositionalDepthGradesQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Computing depth grades for user {UserId} league {LeagueId} rosterOverride {RosterId}",
            request.SleeperUserId, request.SleeperLeagueId, request.SleeperRosterId);

        // 1 — Load roster
        var rosterDoc = !string.IsNullOrEmpty(request.SleeperRosterId)
            ? await rosterPlayerRepository.GetByRosterIdAsync(
                request.SleeperRosterId, request.SleeperLeagueId, cancellationToken)
            : await rosterPlayerRepository.GetBySleeperUserIdAsync(
                request.SleeperUserId, request.SleeperLeagueId, cancellationToken);

        if (rosterDoc is null || rosterDoc.PlayerIds.Count == 0)
            return null;

        var playerIds = rosterDoc.PlayerIds;

        // 2 — Bulk load players, sims, injuries
        var players = await playerRepository.GetBySleeperIdsAsync(playerIds, cancellationToken);
        var simDocs = await simulationRepository.GetLatestBySleeperIdsAsync(
            playerIds, request.Season, cancellationToken);
        var injuries = await injuryAlertRepository.GetActiveAlertsAsync(null, cancellationToken);

        var playerLookup = players.ToDictionary(p => p.SleeperPlayerId!, p => p);
        var simLookup = simDocs.ToDictionary(
            s => s.SleeperPlayerId ?? string.Empty, s => s);
        var injuryLookup = injuries
            .Where(i => i.SleeperPlayerId != null)
            .GroupBy(i => i.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.First());

        // 3 — Grade each position
        var grades = new List<PositionDepthGradeDto>();

        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
        {
            var baseline = PositionBaseline[pos];
            var starterSlots = StarterSlots[pos];
            var qualityFloor = baseline * FillerFloorFraction;

            var posPlayers = playerIds
                .Where(id =>
                {
                    playerLookup.TryGetValue(id, out var p);
                    return p?.Position.ToString() == pos;
                })
                .Select(id =>
                {
                    playerLookup.TryGetValue(id, out var player);
                    simLookup.TryGetValue(id, out var sim);
                    injuryLookup.TryGetValue(id, out var injury);

                    var designation = injury?.Designation;
                    var isOut = designation is "IR" or "Out";
                    var median = sim is not null ? (double)sim.Median : 0.0;
                    var injuryFactor = designation switch
                    {
                        "Q" or "Doubtful" => 0.80,
                        "IR" or "Out" => 0.0,
                        _ => 1.0
                    };

                    return new
                    {
                        Median = median * injuryFactor,
                        RawMedian = median,
                        IsOut = isOut,
                        Designation = designation
                    };
                })
                .OrderByDescending(p => p.Median)
                .ToList();

            var starterGroup = posPlayers.Take(starterSlots).ToList();
            var starterScore = starterGroup.Count > 0
                ? starterGroup.Average(p => p.Median)
                : 0.0;

            double depthScore = 0;
            for (int i = 0; i < posPlayers.Count; i++)
            {
                var isStarterSlot = i < starterSlots;
                if (isStarterSlot || posPlayers[i].Median >= qualityFloor)
                {
                    var weight = i < DepthWeights.Length ? DepthWeights[i] : 0.05;
                    depthScore += posPlayers[i].Median * weight;
                }
            }

            // GRADE-FIX-002: if starter quality is below 50% of baseline,
            // position contributes nothing — prevents filler inflation
            var starterQualityFloor = baseline * 0.50;
            var starterNorm = baseline > 0 ? (starterScore / baseline) * 50.0 : 0;
            var depthNorm = baseline > 0 ? (depthScore / (baseline * starterSlots)) * 30.0 : 0;
            var rawScore = starterScore < starterQualityFloor
                ? 0.0
                : Math.Clamp(starterNorm + depthNorm, 0, 100);

            var (grade, label) = MapGrade((int)Math.Round(rawScore));

            var rosteredCount = posPlayers.Count;
            var healthyCount = posPlayers.Count(p => !p.IsOut);
            var summary = BuildSummary(pos, grade, starterScore, healthyCount, rosteredCount);

            grades.Add(new PositionDepthGradeDto(
                Position: pos,
                Grade: grade,
                GradeScore: (int)Math.Round(rawScore),
                Label: label,
                Summary: summary,
                RosteredCount: rosteredCount,
                HealthyCount: healthyCount,
                StarterScore: Math.Round(starterScore, 1),
                DepthScore: Math.Round(depthScore, 1)));
        }

        return new PositionalDepthGradesDto(Grades: grades);
    }

    private static (string Grade, string Label) MapGrade(int score)
    {
        foreach (var (min, grade, label) in GradeTable)
            if (score >= min) return (grade, label);
        return ("F", "Dire");
    }

    private static string BuildSummary(
        string pos, string grade, double starterScore, int healthy, int rostered)
    {
        var pts = starterScore.ToString("F1");
        return grade switch
        {
            "A+" or "A" => $"Elite {pos} room — starter projects {pts} pts with quality depth behind.",
            "B+" or "B" => $"Solid {pos} position — {pts} pts projected from starter(s), decent depth.",
            "C+" or "C" => healthy < rostered
                ? $"Average {pos} depth with injury concerns — monitor the injury report."
                : $"Average {pos} depth — {pts} pts projected. Waiver wire may help.",
            "D" => $"Weak {pos} room — only {healthy} healthy player(s). Priority waiver target.",
            _ => $"No viable {pos} option. Immediate waiver or trade action needed."
        };
    }
}