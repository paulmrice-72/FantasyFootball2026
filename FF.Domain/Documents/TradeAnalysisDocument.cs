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
    public double ValueDifferential { get; set; }    // positive = you win

    public string Grade { get; set; } = string.Empty;   // A/B/C/D/F
    public string Recommendation { get; set; } = string.Empty;
    public List<string> KeyInsights { get; set; } = [];
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