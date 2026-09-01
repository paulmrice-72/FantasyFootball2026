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

    /// <summary>
    /// Sum of the depth weights (2.25). Used as the depth normalisation reference:
    /// a position stocked with baseline-quality players across every weighted slot
    /// scores exactly the full 30 depth points.
    /// </summary>
    private static readonly double DepthWeightTotal = DepthWeights.Sum();

    private const double MaxDepthPoints = 30.0;
    private const double StarterPointScale = 50.0;

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
                    var isOut = IsOut(designation);
                    var median = sim is not null ? (double)sim.Median : 0.0;

                    return new
                    {
                        Median = median * InjuryFactor(designation),
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
            var starterQualityFloor = baseline * FillerFloorFraction;

            // Starter component is intentionally UNCAPPED. An elite starter scoring
            // well above baseline is how a room reaches A+; clamping this at 50
            // would make the A+ tier unreachable.
            var starterNorm = baseline > 0
                ? (starterScore / baseline) * StarterPointScale
                : 0;

            // ── Depth normalisation (fixed 2026-09-01) ────────────────────────
            //
            // This previously divided by (baseline * starterSlots), which made the
            // denominator depend on how many STARTERS a position requires rather
            // than on how much depth was actually being measured. The numerator
            // sums every rostered player against the depth weights, so the two
            // sides were measuring different things.
            //
            // The effect was a systematic bias toward single-starter positions.
            // Verified against a live roster on 2026-09-01:
            //
            //   QB  2 players -> depth component 41.5   (of a documented max of 30)
            //   TE  2 players -> depth component 40.0
            //   RB  4 players -> depth component 31.5
            //   WR  6 players -> depth component 21.7
            //
            // So a two-man TE room scored 40 depth points and graded A, while a
            // six-man WR room scored 21.7 and graded B+ — the "A's on positions
            // with no depth beyond the starter" reported on 2026-08-31. It was not
            // rewarding quantity over quality; it was dividing by the wrong number.
            //
            // Normalising against the weight total means a position filled with
            // baseline-quality players across every weighted slot earns exactly the
            // documented 30 points, regardless of how many starters it requires.
            var depthNorm = baseline > 0
                ? Math.Min((depthScore / (baseline * DepthWeightTotal)) * MaxDepthPoints, MaxDepthPoints)
                : 0;

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

    // ── Injury designation handling ──────────────────────────────────────
    //
    // The designation string is matched loosely on purpose. The previous version
    // compared against exactly "Q", "Doubtful", "IR" and "Out"; grades computed on
    // 2026-09-01 reproduce exactly only when NO injury factor is applied to three
    // players the UI displayed as "Questionable", so the comparison was not
    // matching the stored values. Matching on a normalised prefix makes the check
    // work for "Q" and "Questionable" alike.
    //
    // NOTE: this being a string mismatch is inferred, not proven — an empty injury
    // lookup would produce the same symptom. If grades still ignore injuries after
    // this change, check what GetActiveAlertsAsync actually returns.

    private static double InjuryFactor(string? designation) =>
        Normalise(designation) switch
        {
            "OUT" => 0.0,
            "DOUBTFUL" => 0.80,
            "QUESTIONABLE" => 0.80,
            _ => 1.0
        };

    private static bool IsOut(string? designation) => Normalise(designation) == "OUT";

    private static string Normalise(string? designation)
    {
        if (string.IsNullOrWhiteSpace(designation)) return string.Empty;

        var d = designation.Trim().ToUpperInvariant();

        if (d is "IR" or "O" or "OUT" || d.StartsWith("OUT")) return "OUT";
        if (d is "D" || d.StartsWith("DOUBT")) return "DOUBTFUL";
        if (d is "Q" || d.StartsWith("QUEST")) return "QUESTIONABLE";

        return d;
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
