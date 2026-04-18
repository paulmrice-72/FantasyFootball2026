// FF.Application/Services/RookieDynastyScoreCalculator.cs
using FF.Domain.Documents;

namespace FF.Application.Services;

public static class RookieDynastyScoreCalculator
{
    private const double DraftCapitalMaxWeight = 25.0;
    private const double FantasyProsMaxWeight = 20.0;
    private const double PffGradeMaxWeight = 20.0;
    private const double ConsensusAdpMaxWeight = 15.0;
    private const double AthleticismMaxWeight = 12.0;
    private const double ValuationBlendMaxWeight = 10.0;

    private const double PositionalFloor = 10.0;
    private const int MaxPick = 262;
    private const int MaxAdpPick = 200;
    private const double MaxAgeBonus = 0.05;
    private const double MaxAgePenalty = -0.08;

    public static double Calculate(
        int? overallPick,
        string position,
        DynastyValuationDocument? valuation,
        int? fantasyProsRank,
        double? pffGrade,
        double? consensusAdp,
        int? age = null,
        double? athleticismScore = null)
    {
        return CalculateWithBreakdown(
            overallPick, position, valuation,
            fantasyProsRank, pffGrade, consensusAdp,
            age, athleticismScore).DynastyScore;
    }

    public static RookieScoreBreakdown CalculateWithBreakdown(
        int? overallPick,
        string position,
        DynastyValuationDocument? valuation,
        int? fantasyProsRank,
        double? pffGrade,
        double? consensusAdp,
        int? age = null,
        double? athleticismScore = null)
    {
        var draftCapitalScore = ScoreDraftCapital(overallPick);
        var fantasyProsScore = ScoreFantasyProsRank(fantasyProsRank);
        var pffScore = ScorePffGrade(pffGrade);
        var adpScore = ScoreConsensusAdp(consensusAdp);
        var valuationScore = ScoreValuationBlend(valuation);
        var positionalScore = ScorePosition(position);
        var athleticismRaw = athleticismScore.HasValue
                                     ? Math.Clamp(athleticismScore.Value, 0, 100)
                                     : 0.0;

        var activeSignals = new List<(double Score, double MaxWeight, string Name)>();

        if (overallPick is > 0)
            activeSignals.Add((draftCapitalScore, DraftCapitalMaxWeight, "DraftCapital"));
        if (fantasyProsRank.HasValue)
            activeSignals.Add((fantasyProsScore, FantasyProsMaxWeight, "FantasyPros"));
        if (pffGrade.HasValue)
            activeSignals.Add((pffScore, PffGradeMaxWeight, "PffGrade"));
        if (consensusAdp.HasValue)
            activeSignals.Add((adpScore, ConsensusAdpMaxWeight, "ConsensusAdp"));
        if (athleticismScore.HasValue)
            activeSignals.Add((athleticismRaw, AthleticismMaxWeight, "Athleticism"));
        if (valuation is not null)
            activeSignals.Add((valuationScore, ValuationBlendMaxWeight, "ValuationBlend"));

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

        composite += positionalScore * (PositionalFloor / 100.0);

        var ageMultiplier = ComputeAgeMultiplier(age, position);
        var adjustedScore = composite * (1.0 + ageMultiplier);
        var dynastyScore = Math.Round(Math.Clamp(adjustedScore, 0, 100), 1);

        return new RookieScoreBreakdown(
            DynastyScore: dynastyScore,
            DraftCapitalScore: Math.Round(draftCapitalScore, 1),
            FantasyProsScore: Math.Round(fantasyProsScore, 1),
            PffGradeScore: Math.Round(pffScore, 1),
            ConsensusAdpScore: Math.Round(adpScore, 1),
            AthleticismScore: Math.Round(athleticismRaw, 1),
            ValuationBlendScore: Math.Round(valuationScore, 1),
            PositionalScore: Math.Round(positionalScore, 1),
            AgeMultiplier: Math.Round(ageMultiplier * 100.0, 1),
            ActiveSignals: activeSignals.Select(s => s.Name).ToList());
    }

    public static double ComputeAgeMultiplier(int? age, string position)
    {
        if (age is null) return 0.0;
        var pos = position.ToUpperInvariant();
        var (bonusPerYear, penaltyPerYear) = pos switch
        {
            "RB" => (0.025, 0.040),
            "WR" or "TE" => (0.020, 0.030),
            "QB" => (0.010, 0.015),
            _ => (0.015, 0.025)
        };
        const int baseline = 22;
        var delta = baseline - age.Value;
        return delta >= 0
            ? Math.Min(delta * bonusPerYear, MaxAgeBonus)
            : Math.Max(delta * penaltyPerYear, MaxAgePenalty);
    }

    public static double ScoreDraftCapital(int? overallPick)
    {
        if (overallPick is null or <= 0) return 0;
        var pick = Math.Min(overallPick.Value, MaxPick);
        return 100.0 * (1.0 - Math.Log(pick) / Math.Log(MaxPick));
    }

    public static double ScorePosition(string position) =>
        position.ToUpperInvariant() switch
        {
            "WR" => 90,
            "QB" => 85,
            "TE" => 75,
            "RB" => 55,
            "K" => 10,
            _ => 40
        };

    public static double ScoreFantasyProsRank(int? rank)
    {
        if (rank is null or <= 0) return 0;
        return Math.Max(0, 100.0 - (rank.Value - 1) * (100.0 / 99.0));
    }

    public static double ScorePffGrade(double? grade)
    {
        if (grade is null) return 0;
        return Math.Clamp(grade.Value, 0, 100);
    }

    public static double ScoreConsensusAdp(double? adp)
    {
        if (adp is null or <= 0) return 0;
        return Math.Max(0, 100.0 * (1.0 - (adp.Value - 1.0) / (MaxAdpPick - 1.0)));
    }

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
    double AthleticismScore,
    double ValuationBlendScore,
    double PositionalScore,
    double AgeMultiplier,
    List<string> ActiveSignals);