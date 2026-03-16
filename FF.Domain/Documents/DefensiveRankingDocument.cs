namespace FF.Domain.Documents;

public class DefensiveRankingDocument
{
    public string Id { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;       // defending team
    public string Position { get; set; } = string.Empty;   // QB, RB, WR, TE
    public int Season { get; set; }
    public int Week { get; set; }                          // week through which this is calculated

    // Raw averages
    public decimal AvgFantasyPointsAllowed { get; set; }   // PPR, season to date
    public decimal AvgFantasyPointsAllowedL4W { get; set; } // last 4 weeks

    // Percentile rank (0-100, higher = tougher matchup)
    public decimal SeasonPercentile { get; set; }
    public decimal L4WPercentile { get; set; }

    // Composite score (0-100)
    public decimal DifficultyScore { get; set; }

    public int GamesAllowed { get; set; }                  // sample size
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}