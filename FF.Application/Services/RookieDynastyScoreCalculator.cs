// FF.Application/Services/RookieDynastyScoreCalculator.cs
using FF.Domain.Documents;

namespace FF.Application.Services;

/// <summary>
/// Signal-based normalized composite scorer for rookie draft candidates.
///
/// Design: each signal has a max weight. Only signals with actual data
/// contribute. Weights are normalized across active signals so the
/// composite always sums to 100% — no dead weight from missing data.
///
/// Positional value is an additive floor (always 10%) and is excluded
/// from normalization so the board is never completely flat.
///
/// Signal max weights (must sum to 90 — positional floor holds the other 10):
///   Draft Capital      25%   — NFL pick slot, log-scaled. Post-draft only.
///   FantasyPros Rank   20%   — Overall rookie rank. Available Jan+.
///   PFF Grade          20%   — 0-100 PFF draft grade. Post-combine (March+).
///   Consensus ADP      15%   — Pick number from NFFC/Underdog/Sleeper. March+.
///   Valuation Blend    10%   — CareerValueScore + TradeValue + DFV. Week 1+.
///
/// Calendar behavior:
///   Jan  (FP only)         → FP 20% normalized to 100% of the 90% pool
///   Mar  (FP + PFF + ADP)  → Three signals share the 90% pool proportionally
///   May  (+ draft pick)    → Four signals; draft capital dominates top picks
///   Wk1  (all signals)     → Full composite, most accurate board
/// </summary>
public static class RookieDynastyScoreCalculator
{
    // ── Signal max weights — must sum to 90 ─────────────────────────────
    private const double DraftCapitalMaxWeight = 25.0;
    private const double FantasyProsMaxWeight = 20.0;
    private const double PffGradeMaxWeight = 20.0;
    private const double ConsensusAdpMaxWeight = 15.0;
    private const double ValuationBlendMaxWeight = 10.0;

    // ── Positional floor — always applied, excluded from normalization ───
    private const double PositionalFloor = 10.0;

    // ── NFL draft size (rounds 1-7, ~262 picks) ──────────────────────────
    private const int MaxPick = 262;

    // ── ADP normalization ceiling (picks beyond this score 0) ────────────
    private const int MaxAdpPick = 200;

    public static double Calculate(
        int? overallPick,
        string position,
        DynastyValuationDocument? valuation,
        int? fantasyProsRank,
        double? pffGrade,
        double? consensusAdp)
    {
        var breakdown = CalculateWithBreakdown(
            overallPick, position, valuation, fantasyProsRank, pffGrade, consensusAdp);
        return breakdown.DynastyScore;
    }

    public static RookieScoreBreakdown CalculateWithBreakdown(
        int? overallPick,
        string position,
        DynastyValuationDocument? valuation,
        int? fantasyProsRank,
        double? pffGrade,
        double? consensusAdp)
    {
        // ── Score each signal (0-100) ────────────────────────────────────
        var draftCapitalScore = ScoreDraftCapital(overallPick);
        var fantasyProsScore = ScoreFantasyProsRank(fantasyProsRank);
        var pffScore = ScorePffGrade(pffGrade);
        var adpScore = ScoreConsensusAdp(consensusAdp);
        var valuationScore = ScoreValuationBlend(valuation);
        var positionalScore = ScorePosition(position);

        // ── Determine which signals are active ───────────────────────────
        var activeSignals = new List<(double Score, double MaxWeight, string Name)>();

        if (overallPick is > 0)
            activeSignals.Add((draftCapitalScore, DraftCapitalMaxWeight, "DraftCapital"));
        if (fantasyProsRank.HasValue)
            activeSignals.Add((fantasyProsScore, FantasyProsMaxWeight, "FantasyPros"));
        if (pffGrade.HasValue)
            activeSignals.Add((pffScore, PffGradeMaxWeight, "PffGrade"));
        if (consensusAdp.HasValue)
            activeSignals.Add((adpScore, ConsensusAdpMaxWeight, "ConsensusAdp"));
        if (valuation is not null)
            activeSignals.Add((valuationScore, ValuationBlendMaxWeight, "ValuationBlend"));

        // ── Normalize active signal weights to fill the 90% pool ─────────
        double composite = 0;
        double totalMaxWeight = activeSignals.Sum(s => s.MaxWeight);

        if (totalMaxWeight > 0)
        {
            foreach (var (score, maxWeight, _) in activeSignals)
            {
                var normalizedWeight = (maxWeight / totalMaxWeight) * 90.0;
                composite += score * (normalizedWeight / 100.0);
            }
        }

        // ── Add positional floor (always 10%) ────────────────────────────
        composite += positionalScore * (PositionalFloor / 100.0);

        var dynastyScore = Math.Round(Math.Clamp(composite, 0, 100), 1);

        return new RookieScoreBreakdown(
            DynastyScore: dynastyScore,
            DraftCapitalScore: Math.Round(draftCapitalScore, 1),
            FantasyProsScore: Math.Round(fantasyProsScore, 1),
            PffGradeScore: Math.Round(pffScore, 1),
            ConsensusAdpScore: Math.Round(adpScore, 1),
            ValuationBlendScore: Math.Round(valuationScore, 1),
            PositionalScore: Math.Round(positionalScore, 1),
            ActiveSignals: activeSignals.Select(s => s.Name).ToList());
    }

    // ── Component scorers ────────────────────────────────────────────────

    /// <summary>
    /// Log-scale: pick 1 = 100, pick 32 ≈ 65, pick 100 ≈ 35, pick 262 = 0.
    /// </summary>
    public static double ScoreDraftCapital(int? overallPick)
    {
        if (overallPick is null or <= 0) return 0;
        var pick = Math.Min(overallPick.Value, MaxPick);
        return 100.0 * (1.0 - Math.Log(pick) / Math.Log(MaxPick));
    }

    /// <summary>
    /// Dynasty positional hierarchy — used as the additive floor only.
    /// WR slight edge for volume/longevity. RB penalized for short career arcs.
    /// </summary>
    public static double ScorePosition(string position) =>
        position.ToUpperInvariant() switch
        {
            "WR" => 90,
            "QB" => 85,
            "TE" => 75,
            "RB" => 55,
            "K" => 10,
            _ => 40   // DEF, IDP, unknown
        };

    /// <summary>
    /// Rank 1 = 100, rank 100 = ~0. Linear decay over 99 slots.
    /// </summary>
    public static double ScoreFantasyProsRank(int? rank)
    {
        if (rank is null or <= 0) return 0;
        return Math.Max(0, 100.0 - (rank.Value - 1) * (100.0 / 99.0));
    }

    /// <summary>
    /// PFF grade is already 0-100. Clamp and pass through.
    /// </summary>
    public static double ScorePffGrade(double? grade)
    {
        if (grade is null) return 0;
        return Math.Clamp(grade.Value, 0, 100);
    }

    /// <summary>
    /// ADP pick 1 = 100, pick MaxAdpPick = 0. Linear decay.
    /// </summary>
    public static double ScoreConsensusAdp(double? adp)
    {
        if (adp is null or <= 0) return 0;
        return Math.Max(0, 100.0 * (1.0 - (adp.Value - 1.0) / (MaxAdpPick - 1.0)));
    }

    /// <summary>
    /// Blends CareerValueScore + TradeValue + DiscountedFutureValue equally.
    /// Each field is already 0-100 normalized in the existing model.
    /// </summary>
    public static double ScoreValuationBlend(DynastyValuationDocument? val)
    {
        if (val is null) return 0;
        var career = Math.Clamp(val.CareerValueScore, 0, 100);
        var trade = Math.Clamp(val.TradeValue, 0, 100);
        var dfv = Math.Clamp(val.DiscountedFutureValue, 0, 100);
        return (career + trade + dfv) / 3.0;
    }
}

public record RookieScoreBreakdown(
    double DynastyScore,
    double DraftCapitalScore,
    double FantasyProsScore,
    double PffGradeScore,
    double ConsensusAdpScore,
    double ValuationBlendScore,
    double PositionalScore,
    List<string> ActiveSignals);