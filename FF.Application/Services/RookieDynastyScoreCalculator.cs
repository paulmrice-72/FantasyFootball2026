// FF.Application/Services/RookieDynastyScoreCalculator.cs
using FF.Domain.Documents;

namespace FF.Application.Services;

/// <summary>
/// Computes a 0-100 composite Dynasty Score for rookie draft candidates.
/// Components:
///   35% — Draft capital (overall pick slot, log-scaled)
///   25% — Positional value (dynasty hierarchy)
///   30% — Dynasty valuation blend (CareerValueScore + TradeValue + DFV)
///   10% — FantasyPros rookie rank
///
/// All components return 0 when source data is unavailable so the board
/// remains usable before NFL draft results are imported.
/// </summary>
public static class RookieDynastyScoreCalculator
{
    // ── Weights ───────────────────────────────────────────────────────────
    private const double DraftCapitalWeight = 0.35;
    private const double PositionalValueWeight = 0.25;
    private const double ValuationBlendWeight = 0.30;
    private const double FantasyProsWeight = 0.10;

    // ── NFL draft size (rounds 1-7, ~262 picks) ───────────────────────────
    private const int MaxPick = 262;

    public static double Calculate(
        int? overallPick,
        string position,
        DynastyValuationDocument? valuation,
        int? fantasyProsRank)
    {
        var draftCapital = ScoreDraftCapital(overallPick);
        var positional = ScorePosition(position);
        var valuationBlend = ScoreValuationBlend(valuation);
        var fpScore = ScoreFantasyProsRank(fantasyProsRank);

        var composite =
            (draftCapital * DraftCapitalWeight) +
            (positional * PositionalValueWeight) +
            (valuationBlend * ValuationBlendWeight) +
            (fpScore * FantasyProsWeight);

        return Math.Round(Math.Clamp(composite, 0, 100), 1);
    }

    /// <summary>
    /// Breakdown version — returns per-component scores for UI transparency.
    /// </summary>
    public static RookieScoreBreakdown CalculateWithBreakdown(
        int? overallPick,
        string position,
        DynastyValuationDocument? valuation,
        int? fantasyProsRank)
    {
        var draftCapital = ScoreDraftCapital(overallPick);
        var positional = ScorePosition(position);
        var valuationBlend = ScoreValuationBlend(valuation);
        var fpScore = ScoreFantasyProsRank(fantasyProsRank);

        var composite =
            (draftCapital * DraftCapitalWeight) +
            (positional * PositionalValueWeight) +
            (valuationBlend * ValuationBlendWeight) +
            (fpScore * FantasyProsWeight);

        return new RookieScoreBreakdown(
            DynastyScore: Math.Round(Math.Clamp(composite, 0, 100), 1),
            DraftCapitalScore: Math.Round(draftCapital, 1),
            PositionalScore: Math.Round(positional, 1),
            ValuationBlendScore: Math.Round(valuationBlend, 1),
            FantasyProsScore: Math.Round(fpScore, 1));
    }

    // ── Component scorers ─────────────────────────────────────────────────

    /// <summary>
    /// Log-scale: pick 1 = 100, pick 32 ≈ 65, pick 100 ≈ 35, pick 262 = 0.
    /// Null pick (pre-draft) = 0.
    /// </summary>
    public static double ScoreDraftCapital(int? overallPick)
    {
        if (overallPick is null or <= 0) return 0;
        var pick = Math.Min(overallPick.Value, MaxPick);
        // Log curve: 100 * (1 - log(pick) / log(MaxPick))
        return 100.0 * (1.0 - Math.Log(pick) / Math.Log(MaxPick));
    }

    /// <summary>
    /// Dynasty positional hierarchy.
    /// WR and QB are roughly co-equal at top — WR slight edge for volume.
    /// RB is penalized for short career arcs.
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
    /// Blends three dynasty_valuations fields equally.
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

    /// <summary>
    /// Rank 1 = 100, rank 50 = 50, rank 100+ = 0. Linear decay.
    /// Null rank (not in FP rankings) = 0.
    /// </summary>
    public static double ScoreFantasyProsRank(int? rank)
    {
        if (rank is null or <= 0) return 0;
        return Math.Max(0, 100.0 - (rank.Value - 1) * (100.0 / 99.0));
    }
}

public record RookieScoreBreakdown(
    double DynastyScore,
    double DraftCapitalScore,
    double PositionalScore,
    double ValuationBlendScore,
    double FantasyProsScore);