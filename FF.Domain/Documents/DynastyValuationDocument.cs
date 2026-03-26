using FF.Domain.Enums;

namespace FF.Domain.Documents;

public class DynastyValuationDocument
{
    public string Id { get; set; } = string.Empty;
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;        // nflfastR ID
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public int Age { get; set; }
    public int? YearsExperience { get; set; }
    public int Season { get; set; }

    // ── Breakout Signal (PBI-032) ─────────────────────────────────────────
    public double BreakoutScore { get; set; }                   // 0-100
    public BreakoutClassification BreakoutClassification { get; set; }
    public List<string> BreakoutSignals { get; set; } = [];     // human-readable signal labels
    public DateTime BreakoutScoredAt { get; set; }

    // ── Discounted Future Value (PBI-033) — populated later ───────────────
    public double TradeValue { get; set; }                      // 0-100 normalized
    public double DiscountedFutureValue { get; set; }
    public DateTime? TradeValueComputedAt { get; set; }

    // ── Career Sim reference ──────────────────────────────────────────────
    public double CareerValueScore { get; set; }
    public int PeakYear { get; set; }
    public double YearsOfPrimeRemaining { get; set; }
    public CareerPhase CareerPhase { get; set; }
}