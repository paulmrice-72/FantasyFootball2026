// FF.Application/Features/Team/Queries/GetStartSitRecommendationsQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

public class GetStartSitRecommendationsQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IInjuryAlertRepository injuryAlertRepository,
    ILeagueRepository leagueRepository,
    ILogger<GetStartSitRecommendationsQueryHandler> logger)
    : IRequestHandler<GetStartSitRecommendationsQuery, StartSitRecommendationsDto?>
{
    public async Task<StartSitRecommendationsDto?> Handle(
        GetStartSitRecommendationsQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building Start/Sit for user {UserId} league {LeagueId} week {Week}",
            request.SleeperUserId, request.SleeperLeagueId, request.Week);

        // 1 — Load roster
        var rosterDoc = await rosterPlayerRepository.GetBySleeperUserIdAsync(
            request.SleeperUserId, request.SleeperLeagueId, cancellationToken);
        if (rosterDoc is null || rosterDoc.PlayerIds.Count == 0)
            return null;

        // 2 — Load league roster configuration
        // TO:
        var league = await leagueRepository
            .GetBySleeperIdAsync(request.SleeperLeagueId, request.Season, cancellationToken);
        var rosterConfig = league?.GetRosterConfiguration()
            ?? RosterConfiguration.Standard;

        // Build slot counts from config
        var starterSlots = new Dictionary<string, int>
        {
            ["QB"] = rosterConfig.QbSlots,
            ["RB"] = rosterConfig.RbSlots,
            ["WR"] = rosterConfig.WrSlots,
            ["TE"] = rosterConfig.TeSlots,
        };

        // All positions eligible in any flex slot (union across all flex definitions)
        var flexEligible = rosterConfig.FlexSlotDefinitions
            .SelectMany(f => f.EligiblePositions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var flexSlotCount = rosterConfig.FlexSlotDefinitions.Count;

        var playerIds = rosterDoc.PlayerIds;

        // 3 — Bulk load players, sims, injuries
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

        // 4 — Build enriched player list (exclude IR/Out)
        var starterSet = rosterDoc.StarterIds.ToHashSet();
        var enriched = playerIds
            .Where(id =>
            {
                injuryLookup.TryGetValue(id, out var inj);
                return inj?.Designation is not ("IR" or "Out");
            })
            .Select(id =>
            {
                playerLookup.TryGetValue(id, out var player);
                simLookup.TryGetValue(id, out var sim);
                injuryLookup.TryGetValue(id, out var injury);

                return new EnrichedPlayer(
                    SleeperPlayerId: id,
                    PlayerName: player?.FullName ?? "Unknown",
                    Position: player?.Position.ToString() ?? "?",
                    Median: sim is not null ? (double)sim.Median : 0,
                    Floor: sim is not null ? (double)sim.Floor : 0,
                    Ceiling: sim is not null ? (double)sim.Ceiling : 0,
                    BoomProbability: sim is not null ? (double)sim.BoomProbability : 0,
                    BustProbability: sim is not null ? (double)sim.BustProbability : 0,
                    NflTeam: player?.NflTeam ?? "—",
                    InjuryDesignation: injury?.Designation,
                    IsCurrentStarter: starterSet.Contains(id));
            })
            .ToList();

        // 5 — Generate decisions per dedicated position slot
        var decisions = new List<StartSitDecisionDto>();

        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
        {
            if (!starterSlots.TryGetValue(pos, out var slots) || slots == 0)
                continue;

            var posPlayers = enriched
                .Where(p => p.Position == pos)
                .OrderByDescending(p => p.Median)
                .ToList();

            if (posPlayers.Count <= slots) continue;

            var ranked = RankAndScore(posPlayers);

            for (var slotIndex = slots - 1; slotIndex < posPlayers.Count - 1; slotIndex++)
            {
                var slotLabel = slots == 1 ? pos : $"{pos}{slotIndex + 1}";
                var contenders = ranked
                    .Skip(Math.Max(0, slotIndex - 1))
                    .Take(3)
                    .ToList();

                if (contenders.Count < 2) continue;

                decisions.Add(new StartSitDecisionDto(
                    Position: pos,
                    SlotLabel: slotLabel,
                    Options: contenders));
                break;
            }
        }

        // 6 — FLEX decisions — one per flex slot definition
        foreach (var flexDef in rosterConfig.FlexSlotDefinitions)
        {
            // Label: SUPERFLEX if QB eligible, otherwise FLEX
            var slotLabel = flexDef.IsEligible("QB") ? "SUPERFLEX" : "FLEX";

            var flexBubble = enriched
                .Where(p => flexDef.IsEligible(p.Position))
                .OrderByDescending(p => p.Median)
                .ToList();

            // Bubble = last starter + first bench at each eligible position
            var flexContenders = new List<EnrichedPlayer>();
            foreach (var pos in flexDef.EligiblePositions)
            {
                starterSlots.TryGetValue(pos, out var posSlots);
                var posGroup = flexBubble.Where(p => p.Position == pos).ToList();
                if (posGroup.Count > posSlots)
                {
                    flexContenders.Add(posGroup[posSlots - 1]); // last starter
                    flexContenders.Add(posGroup[posSlots]);      // first bench
                }
            }

            if (flexContenders.Count < 2) continue;

            var flexRanked = RankAndScore(
                flexContenders.DistinctBy(p => p.SleeperPlayerId).ToList());

            decisions.Add(new StartSitDecisionDto(
                Position: slotLabel,
                SlotLabel: slotLabel,
                Options: flexRanked.Take(3).ToList()));
        }

        return new StartSitRecommendationsDto(
            Week: request.Week,
            Season: request.Season,
            Decisions: decisions);
    }

    // ── Scoring engine ────────────────────────────────────────────────────
    private static List<StartSitOptionDto> RankAndScore(List<EnrichedPlayer> players)
    {
        if (players.Count == 0) return [];

        var options = new List<StartSitOptionDto>();

        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];

            var compositeScore =
                (p.Median * 0.50) +
                (p.Floor * 0.20) +
                (p.Ceiling * 0.15) +
                (p.BoomProbability * 20 * 0.10) -
                (p.BustProbability * 20 * 0.05);

            var injuryPenalty = p.InjuryDesignation switch
            {
                "Q" or "Doubtful" => 0.15,
                _ => 0.0
            };
            compositeScore *= (1.0 - injuryPenalty);

            var confidence = i == 0 && players.Count > 1
                ? Math.Min(100, (int)((compositeScore - CalcComposite(players[1]))
                    / Math.Max(1, compositeScore) * 100 * 3))
                : Math.Max(0, 50 - (i * 20));
            confidence = Math.Clamp(confidence, 5, 95);

            var verdict = (i, confidence) switch
            {
                (0, >= 60) => StartSitVerdict.Start,
                (0, _) => StartSitVerdict.LeanStart,
                (1, _) => StartSitVerdict.LeanSit,
                _ => StartSitVerdict.Sit
            };

            var confidenceLabel = confidence >= 70 ? "High"
                : confidence >= 40 ? "Medium"
                : "Low";

            options.Add(new StartSitOptionDto(
                SleeperPlayerId: p.SleeperPlayerId,
                PlayerName: p.PlayerName,
                Position: p.Position,
                NflTeam: p.NflTeam,
                Verdict: verdict,
                ConfidenceScore: confidence,
                ConfidenceLabel: confidenceLabel,
                Median: p.Median,
                Floor: p.Floor,
                Ceiling: p.Ceiling,
                BoomProbability: p.BoomProbability,
                BustProbability: p.BustProbability,
                InjuryDesignation: p.InjuryDesignation,
                Rationale: BuildRationale(p, i, players, confidence)));
        }

        return options;
    }

    private static double CalcComposite(EnrichedPlayer p) =>
        (p.Median * 0.50) +
        (p.Floor * 0.20) +
        (p.Ceiling * 0.15) +
        (p.BoomProbability * 20 * 0.10) -
        (p.BustProbability * 20 * 0.05);

    private static string BuildRationale(
        EnrichedPlayer p, int rank, List<EnrichedPlayer> all, int confidence)
    {
        if (!string.IsNullOrEmpty(p.InjuryDesignation)
            && p.InjuryDesignation is "Q" or "Doubtful")
            return $"Questionable — monitor injury report. Median {p.Median:F1} pts projected.";

        if (rank == 0)
        {
            var gap = all.Count > 1 ? p.Median - all[1].Median : 0;
            return gap >= 4
                ? $"Clear start — projects {p.Median:F1} pts, {gap:F1} pts ahead of next option."
                : $"Lean start — {p.Median:F1} pts projected, slight edge over alternatives.";
        }

        if (rank == 1)
        {
            var leader = all[0];
            return p.BoomProbability > leader.BoomProbability + 0.10
                ? $"Ceiling play — boom probability {p.BoomProbability:P0} vs {leader.BoomProbability:P0}. High risk."
                : $"Sit — projects {p.Median:F1} pts, behind {leader.PlayerName} ({leader.Median:F1} pts).";
        }

        return $"Sit — {p.Median:F1} pts projected, lower-priority option this week.";
    }

    private record EnrichedPlayer(
        string SleeperPlayerId,
        string PlayerName,
        string Position,
        double Median,
        double Floor,
        double Ceiling,
        double BoomProbability,
        double BustProbability,
        string NflTeam,
        string? InjuryDesignation,
        bool IsCurrentStarter);
}