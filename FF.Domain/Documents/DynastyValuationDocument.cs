using FF.Domain.Enums;

namespace FF.Domain.Documents;

public class DynastyValuationDocument
{
    public string Id { get; set; } = string.Empty;
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public int Age { get; set; }
    public int? YearsExperience { get; set; }
    public int Season { get; set; }

    // ── Scoring format this valuation was calculated for ─────────────────
    public ScoringFormat ScoringFormat { get; set; } = ScoringFormat.HalfPpr;

    // ── Breakout Signal ──────────────────────────────────────────────────
    public double BreakoutScore { get; set; }
    public BreakoutClassification BreakoutClassification { get; set; }
    public List<string> BreakoutSignals { get; set; } = [];
    public DateTime BreakoutScoredAt { get; set; }

    // ── Discounted Future Value ──────────────────────────────────────────
    public double TradeValue { get; set; }
    public double DiscountedFutureValue { get; set; }
    public DateTime? TradeValueComputedAt { get; set; }

    // ── Career Sim reference ─────────────────────────────────────────────
    public double CareerValueScore { get; set; }
    public int PeakYear { get; set; }
    public double YearsOfPrimeRemaining { get; set; }
    public CareerPhase CareerPhase { get; set; }
}