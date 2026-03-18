// FF.Domain/Documents/PlayerProjectionDocument.cs
namespace FF.Domain.Documents;

public class PlayerProjectionDocument
{
    public string Id { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;       // gsis_id
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public string OpponentTeam { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Week { get; set; }

    // Projected fantasy points (all scoring formats)
    public decimal ProjectedPoints { get; set; }
    public decimal ProjectedPointsPpr { get; set; }
    public decimal ProjectedPointsHalfPpr { get; set; }

    // Model inputs (stored for transparency / tuning)
    public decimal WeightedAvgPoints { get; set; }
    public decimal MatchupAdjustmentFactor { get; set; }
    public decimal SnapPctInput { get; set; }
    public decimal TargetShareInput { get; set; }

    // Regression metadata
    public int GameSampleSize { get; set; }
    public decimal RSquared { get; set; }
    public string ScoringFormat { get; set; } = "HalfPpr";
    public DateTime CalculatedAt { get; set; }
    // Game script context — populated when Vegas spread data is available
    // Defaults to Competitive (neutral) when spread is unknown
    public string GameScript { get; set; } = "Unknown";
    public decimal RbVolumeMultiplier { get; set; } = 1.0m;
    public decimal WrTeVolumeMultiplier { get; set; } = 1.0m;
    public decimal SpreadInput { get; set; } = 0m;
}