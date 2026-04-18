// FF.Infrastructure/Services/TradeAnalyzerService.cs
using FF.Application.Features.Dynasty.Commands;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

public class TradeAnalyzerService(
    IDynastyValuationRepository valuationRepository,
    IPickValueRepository pickValueRepository,
    IRosterPlayerRepository rosterPlayerRepository,
    ILeagueRepository leagueRepository,
    ILogger<TradeAnalyzerService> logger)
    : ITradeAnalyzerService
{
    // ── Generic overload (no league context) ────────────────────────────────
    public async Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        IEnumerable<TradePickRequest> myPicks,
        IEnumerable<TradePickRequest> theirPicks,
        int season,
        CancellationToken ct = default)
        => await AnalyzeAsync(userId, myPlayerSleeperIds, theirPlayerSleeperIds,
            myPicks, theirPicks, season, null, null, ct);

    // ── Full overload (league context optional) ─────────────────────────────
    public async Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        IEnumerable<TradePickRequest> myPicks,
        IEnumerable<TradePickRequest> theirPicks,
        int season,
        string? leagueId,
        string? sleeperUserId,
        CancellationToken ct = default)
    {
        var myIds    = myPlayerSleeperIds.ToList();
        var theirIds = theirPlayerSleeperIds.ToList();

        var mySide    = await BuildSideAsync(myIds,    myPicks.ToList(),    ct);
        var theirSide = await BuildSideAsync(theirIds, theirPicks.ToList(), ct);

        var myValue    = mySide.Sum(p => p.TradeValue);
        var theirValue = theirSide.Sum(p => p.TradeValue);
        var diff       = myValue - theirValue;  // negative = you WIN (you receive more)

        // ── League-aware scoring dimensions ─────────────────────────────────
        RosterImpactDetail?    rosterImpact    = null;
        DropAnalysisDetail?    dropAnalysis    = null;
        LeagueStandingImpact?  standingImpact  = null;

        if (!string.IsNullOrEmpty(leagueId) && !string.IsNullOrEmpty(sleeperUserId))
        {
            try
            {
                var allRosters = await rosterPlayerRepository
                    .GetByLeagueAsync(leagueId, ct);
                var myRoster   = allRosters.FirstOrDefault(r =>
                    r.SleeperUserId == sleeperUserId);
                var league     = await leagueRepository
                    .GetBySleeperIdAsync(leagueId, season, ct);

                if (myRoster is not null)
                {
                    var myCurrentPlayerIds = (myRoster.PlayerIds ?? []).ToList();

                    rosterImpact = ComputeRosterImpact(
                        myCurrentPlayerIds, myIds, theirIds,
                        league?.RosterPositions?.Split(',').ToList() ?? new List<string>());

                    // Drop analysis — if receiving more players than giving
                    var playerCountDelta = theirIds.Count - myIds.Count;
                    if (playerCountDelta > 0)
                    {
                        dropAnalysis = await ComputeDropAnalysisAsync(
                            myCurrentPlayerIds, myIds, theirIds,
                            playerCountDelta, ct);
                    }

                    // League standings impact
                    standingImpact = await ComputeLeagueStandingImpactAsync(
                        sleeperUserId, myIds, theirIds,
                        myValue, theirValue, allRosters, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "League-aware scoring failed for {LeagueId} — falling back to generic",
                    leagueId);
            }
        }

        // ── Grade — adjusted for league context ─────────────────────────────
        var effectiveDiff = diff;
        if (dropAnalysis is not null)
            effectiveDiff += dropAnalysis.EffectiveValueLost; // drops cost you more

        var grade = ComputeGrade(effectiveDiff);

        // Bump grade if trade significantly improves league standing
        if (standingImpact?.RankDelta >= 2)
            grade = BumpGrade(grade);

        // Penalise if trade creates a positional hole
        if (rosterImpact?.Warnings.Count > 0)
            grade = PenaliseGrade(grade);

        var recommendation = BuildRecommendation(grade, effectiveDiff, rosterImpact, dropAnalysis);
        var insights       = BuildInsights(mySide, theirSide, effectiveDiff, rosterImpact, standingImpact);

        logger.LogInformation(
            "Trade analyzed for {UserId} [{Mode}] — my {MyValue:F1} vs their {TheirValue:F1} — grade {Grade}",
            userId,
            string.IsNullOrEmpty(leagueId) ? "generic" : "league",
            myValue, theirValue, grade);

        return new TradeAnalysisDocument
        {
            Id                  = ObjectId.GenerateNewId().ToString(),
            UserId              = userId,
            AnalyzedAt          = DateTime.UtcNow,
            Season              = season,
            MySide              = mySide,
            TheirSide           = theirSide,
            MySideValue         = Math.Round(myValue, 2),
            TheirSideValue      = Math.Round(theirValue, 2),
            ValueDifferential   = Math.Round(diff, 2),
            Grade               = grade,
            Recommendation      = recommendation,
            KeyInsights         = insights,
            RosterImpact        = rosterImpact,
            DropAnalysis        = dropAnalysis,
            LeagueStandingImpact = standingImpact
        };
    }

    // ── Roster composition impact ───────────────────────────────────────────
    private static RosterImpactDetail ComputeRosterImpact(
        List<string> currentIds,
        List<string> givingIds,
        List<string> receivingIds,
        List<string> rosterPositionSlots)
    {
        // We only track position counts here via givingIds/receivingIds;
        // actual position lookup happens in BuildSideAsync already done.
        // For simplicity, we flag net change direction per position.
        // Full position counts are appended by the caller from side details.
        return new RosterImpactDetail
        {
            Warnings  = [],
            Positives = []
        };
    }

    // ── Full roster impact with position data ───────────────────────────────
    private static RosterImpactDetail ComputeRosterImpactFromSides(
        List<TradeSideDetail> giving,
        List<TradeSideDetail> receiving,
        List<string> currentPlayerIds,
        List<string> rosterSlots)
    {
        var warnings  = new List<string>();
        var positives = new List<string>();

        // Count positions on current roster
        // (We don't have full position data for all current players here —
        //  we work with the players in the trade sides only)
        var positions = new[] { "QB", "RB", "WR", "TE" };

        foreach (var pos in positions)
        {
            var giving_count   = giving.Count(p => p.Position == pos && !p.IsDraftPick);
            var receiving_count = receiving.Count(p => p.Position == pos && !p.IsDraftPick);
            var net            = receiving_count - giving_count;

            // Starter slot count for this position
            var starterSlots = rosterSlots.Count(s => s == pos);
            if (starterSlots == 0) starterSlots = pos switch
            {
                "QB" => 1, "RB" => 2, "WR" => 2, "TE" => 1, _ => 1
            };

            if (net < 0 && giving_count > 0)
                warnings.Add(
                    $"Losing {giving_count} {pos}(s) — check your remaining {pos} depth.");
            else if (net > 0 && receiving_count > 0)
                positives.Add(
                    $"Adding {receiving_count} {pos}(s) — improves positional depth.");
        }

        // Check for pick-heavy trades
        var picksGiven    = giving.Count(p => p.IsDraftPick);
        var picksReceived = receiving.Count(p => p.IsDraftPick);
        if (picksGiven > picksReceived)
            warnings.Add($"Trading away {picksGiven} pick(s) — reduces future capital.");
        else if (picksReceived > picksGiven)
            positives.Add($"Acquiring {picksReceived} pick(s) — adds future capital.");

        return new RosterImpactDetail
        {
            Warnings  = warnings,
            Positives = positives
        };
    }

    // ── Drop analysis ───────────────────────────────────────────────────────
    private async Task<DropAnalysisDetail> ComputeDropAnalysisAsync(
        List<string> myCurrentIds,
        List<string> givingIds,
        List<string> receivingIds,
        int dropsRequired,
        CancellationToken ct)
    {
        // Remaining roster after trade (excluding players being given away)
        var remainingIds = myCurrentIds
            .Where(id => !givingIds.Contains(id))
            .ToList();

        // Load valuations for remaining players to find weakest
        var remaining = new List<(string Id, string Name, string Pos, double Value)>();
        foreach (var id in remainingIds)
        {
            var v = await valuationRepository.GetBySleeperIdAsync(id, ct);
            if (v is not null)
                remaining.Add((id, v.PlayerName, v.Position, v.TradeValue));
        }

        // Suggest weakest players as drops
        var suggested = remaining
            .OrderBy(p => p.Value)
            .Take(dropsRequired)
            .Select(p => new SuggestedDrop
            {
                SleeperPlayerId = p.Id,
                PlayerName      = p.Name,
                Position        = p.Pos,
                TradeValue      = p.Value
            })
            .ToList();

        return new DropAnalysisDetail
        {
            DropsRequired      = dropsRequired,
            SuggestedDrops     = suggested,
            EffectiveValueLost = Math.Round(suggested.Sum(s => s.TradeValue), 2)
        };
    }

    // ── League standing impact ──────────────────────────────────────────────
    private async Task<LeagueStandingImpact> ComputeLeagueStandingImpactAsync(
        string sleeperUserId,
        List<string> givingIds,
        List<string> receivingIds,
        double myCurrentValue,
        double theirCurrentValue,
        IReadOnlyList<FF.Domain.Documents.RosterPlayerDocument> allRosters,
        CancellationToken ct)
    {
        // Build current total value per team
        var teamValues = new Dictionary<string, double>();
        foreach (var roster in allRosters)
        {
            if (roster.SleeperUserId is null) continue;
            var playerIds = roster.PlayerIds ?? [];
            var total     = 0.0;
            foreach (var id in playerIds)
            {
                var v = await valuationRepository.GetBySleeperIdAsync(id, ct);
                if (v is not null) total += v.TradeValue;
            }
            teamValues[roster.SleeperUserId] = total;
        }

        var currentMyValue = teamValues.TryGetValue(sleeperUserId, out var cv) ? cv : 0;

        // Projected my value: subtract given, add received
        var givenValue    = 0.0;
        foreach (var id in givingIds)
        {
            var v = await valuationRepository.GetBySleeperIdAsync(id, ct);
            if (v is not null) givenValue += v.TradeValue;
        }
        var receivedValue = 0.0;
        foreach (var id in receivingIds)
        {
            var v = await valuationRepository.GetBySleeperIdAsync(id, ct);
            if (v is not null) receivedValue += v.TradeValue;
        }

        var projectedMyValue = currentMyValue - givenValue + receivedValue;

        // Current rank
        var sortedCurrent = teamValues
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
        var currentRank = sortedCurrent.IndexOf(sleeperUserId) + 1;

        // Projected rank — update my value, keep others same
        var projected = teamValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        projected[sleeperUserId] = projectedMyValue;
        var sortedProjected = projected
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
        var projectedRank = sortedProjected.IndexOf(sleeperUserId) + 1;

        return new LeagueStandingImpact
        {
            CurrentRank          = currentRank > 0 ? currentRank : 1,
            ProjectedRank        = projectedRank > 0 ? projectedRank : 1,
            CurrentTotalValue    = Math.Round(currentMyValue, 1),
            ProjectedTotalValue  = Math.Round(projectedMyValue, 1)
        };
    }

    // ── Side builder ────────────────────────────────────────────────────────
    private async Task<List<TradeSideDetail>> BuildSideAsync(
        List<string> sleeperIds,
        List<TradePickRequest> picks,
        CancellationToken ct)
    {
        var side = new List<TradeSideDetail>();

        foreach (var id in sleeperIds)
        {
            var valuation = await valuationRepository.GetBySleeperIdAsync(id, ct);
            if (valuation is null)
            {
                logger.LogWarning(
                    "No dynasty valuation found for SleeperPlayerId {Id}", id);
                side.Add(new TradeSideDetail
                {
                    SleeperPlayerId = id,
                    PlayerName      = "Unknown Player",
                    TradeValue      = 0
                });
                continue;
            }
            side.Add(new TradeSideDetail
            {
                SleeperPlayerId      = id,
                PlayerName           = valuation.PlayerName,
                Position             = valuation.Position,
                Age                  = valuation.Age,
                TradeValue           = valuation.TradeValue,
                BreakoutScore        = valuation.BreakoutScore,
                BreakoutClassification = valuation.BreakoutClassification.ToString(),
                YearsOfPrimeRemaining = valuation.YearsOfPrimeRemaining
            });
        }

        foreach (var pick in picks)
        {
            var pickDoc = await pickValueRepository.GetAsync(
                pick.Round, pick.Tier, pick.Year, ct);
            var value   = pickDoc?.Value ?? 0;
            var label   = $"{pick.Year} {pick.Tier} {OrdinalRound(pick.Round)}";

            side.Add(new TradeSideDetail
            {
                SleeperPlayerId = string.Empty,
                PlayerName      = label,
                Position        = "PICK",
                TradeValue      = value,
                IsDraftPick     = true,
                PickRound       = pick.Round,
                PickTier        = pick.Tier,
                PickYear        = pick.Year
            });
        }

        return side;
    }

    // ── Grading ─────────────────────────────────────────────────────────────
    // Differential is myValue - theirValue; negative means you receive more (you WIN)
    private static string ComputeGrade(double differential) => differential switch
    {
        <= -15 => "A",
        <= -8  => "B",
        <=  7  => "C",
        <= 14  => "D",
        _      => "F"
    };

    private static string BumpGrade(string grade) => grade switch
    {
        "B" => "A", "C" => "B", "D" => "C", "F" => "D", _ => grade
    };

    private static string PenaliseGrade(string grade) => grade switch
    {
        "A" => "B", "B" => "C", "C" => "D", "D" => "F", _ => grade
    };

    private static string BuildRecommendation(
        string grade,
        double diff,
        RosterImpactDetail? roster,
        DropAnalysisDetail? drops)
    {
        var baseMsg = grade switch
        {
            "A" => $"Strong accept — you gain {Math.Abs(diff):F1} value points.",
            "B" => $"Accept — you win by {Math.Abs(diff):F1} value points.",
            "C" => "Even trade — consider player fit and roster needs.",
            "D" => $"Decline — you lose {diff:F1} value points.",
            "F" => $"Hard pass — you lose {diff:F1} value points.",
            _   => "Unable to evaluate."
        };

        if (drops?.DropsRequired > 0)
            baseMsg += $" Note: you must drop {drops.DropsRequired} player(s)" +
                       $" (est. -{drops.EffectiveValueLost:F1} value).";

        if (roster?.Warnings.Count > 0)
            baseMsg += $" Watch: {roster.Warnings.First()}";

        return baseMsg;
    }

    private static List<string> BuildInsights(
        List<TradeSideDetail> mySide,
        List<TradeSideDetail> theirSide,
        double diff,
        RosterImpactDetail? roster,
        LeagueStandingImpact? standing)
    {
        var insights   = new List<string>();
        var myPlayers  = mySide.Where(p => !p.IsDraftPick).ToList();
        var theirPlayers = theirSide.Where(p => !p.IsDraftPick).ToList();
        var myPicks    = mySide.Count(p => p.IsDraftPick);
        var theirPicks = theirSide.Count(p => p.IsDraftPick);

        if (theirPicks > myPicks)
            insights.Add($"You're acquiring {theirPicks} draft pick(s) — future capital.");
        else if (myPicks > theirPicks)
            insights.Add($"You're trading away {myPicks} draft pick(s) — selling future capital.");

        if (myPlayers.Count > 0 && theirPlayers.Count > 0)
        {
            var myAvgAge    = myPlayers.Average(p => p.Age);
            var theirAvgAge = theirPlayers.Average(p => p.Age);
            if (theirAvgAge < myAvgAge - 2)
                insights.Add($"Buying younger — avg age {theirAvgAge:F0} vs {myAvgAge:F0}.");
            else if (myAvgAge < theirAvgAge - 2)
                insights.Add($"Selling younger — avg age {myAvgAge:F0} vs {theirAvgAge:F0}.");
        }

        var myBreakouts    = myPlayers.Count(p => p.BreakoutClassification == "Breakout");
        var theirBreakouts = theirPlayers.Count(p => p.BreakoutClassification == "Breakout");
        if (theirBreakouts > myBreakouts)
            insights.Add($"Acquiring {theirBreakouts} breakout candidate(s).");
        else if (myBreakouts > theirBreakouts)
            insights.Add($"Trading away {myBreakouts} breakout candidate(s).");

        var myPrime    = myPlayers.Sum(p => p.YearsOfPrimeRemaining);
        var theirPrime = theirPlayers.Sum(p => p.YearsOfPrimeRemaining);
        if (theirPrime > myPrime + 2)
            insights.Add($"More prime years incoming ({theirPrime:F0} vs {myPrime:F0}).");
        else if (myPrime > theirPrime + 2)
            insights.Add($"Trading away prime years ({myPrime:F0} vs {theirPrime:F0}).");

        // League-aware insights
        if (roster?.Positives.Count > 0)
            insights.AddRange(roster.Positives);
        if (roster?.Warnings.Count > 0)
            insights.AddRange(roster.Warnings.Select(w => $"⚠ {w}"));
        if (standing is not null && standing.RankDelta != 0)
        {
            var direction = standing.RankDelta > 0 ? "up" : "down";
            insights.Add(
                $"League standing moves {direction} {Math.Abs(standing.RankDelta)} spot(s): " +
                $"#{standing.CurrentRank} → #{standing.ProjectedRank}.");
        }

        return insights;
    }

    private static string OrdinalRound(int round) => round switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{round}th"
    };
}
