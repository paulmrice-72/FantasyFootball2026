// FF.Domain/Documents/TradeAnalysisDocument.cs
namespace FF.Domain.Documents;

public class TradeAnalysisDocument
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; }
    public int Season { get; set; }
    public List<TradeSideDetail> MySide { get; set; } = [];
    public List<TradeSideDetail> TheirSide { get; set; } = [];
    public double MySideValue { get; set; }
    public double TheirSideValue { get; set; }
    public double ValueDifferential { get; set; }   // positive = you win
    public string Grade { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public List<string> KeyInsights { get; set; } = [];

    // ── League-aware additions (null when analyzed without league context) ──
    public RosterImpactDetail? RosterImpact { get; set; }
    public DropAnalysisDetail? DropAnalysis { get; set; }
    public LeagueStandingImpact? LeagueStandingImpact { get; set; }
}

public class TradeSideDetail
{
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int Age { get; set; }
    public double TradeValue { get; set; }
    public double BreakoutScore { get; set; }
    public string BreakoutClassification { get; set; } = string.Empty;
    public double YearsOfPrimeRemaining { get; set; }

    // Draft pick fields — null when item is a player
    public bool IsDraftPick { get; set; } = false;
    public int? PickRound { get; set; }
    public string? PickTier { get; set; }
    public int? PickYear { get; set; }
}

// ── League-aware result types ────────────────────────────────────────────────

public class RosterImpactDetail
{
    public Dictionary<string, int> PositionsBefore { get; set; } = [];
    public Dictionary<string, int> PositionsAfter { get; set; } = [];
    public List<string> Warnings { get; set; } = [];   // e.g. "RB depth drops below recommended"
    public List<string> Positives { get; set; } = [];  // e.g. "WR depth improves to 5"
}

public class DropAnalysisDetail
{
    public int DropsRequired { get; set; }
    public List<SuggestedDrop> SuggestedDrops { get; set; } = [];
    public double EffectiveValueLost { get; set; }   // sum of suggested drop trade values
}

public class SuggestedDrop
{
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public double TradeValue { get; set; }
}

public class LeagueStandingImpact
{
    public int CurrentRank { get; set; }
    public int ProjectedRank { get; set; }
    public double CurrentTotalValue { get; set; }
    public double ProjectedTotalValue { get; set; }
    public int RankDelta => CurrentRank - ProjectedRank;  // positive = improvement
}
