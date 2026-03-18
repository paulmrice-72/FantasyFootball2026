// FF.Domain/Documents/PlayerUsageMetricsDocument.cs
using FF.Domain.Enums;

namespace FF.Domain.Documents;

public class PlayerUsageMetricsDocument
{
    public string Id { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public int Season { get; set; }

    // Target Share
    public decimal TargetShare3Wk { get; set; }
    public decimal TargetShare5Wk { get; set; }
    public decimal TargetShareSeason { get; set; }

    // Air Yards Share
    public decimal AirYardsShare3Wk { get; set; }
    public decimal AirYardsShare5Wk { get; set; }
    public decimal AirYardsShareSeason { get; set; }

    // WOPR
    public decimal Wopr3Wk { get; set; }
    public decimal Wopr5Wk { get; set; }
    public decimal WoprSeason { get; set; }

    // Carry Share
    public decimal CarryShare3Wk { get; set; }
    public decimal CarryShare5Wk { get; set; }
    public decimal CarryShareSeason { get; set; }

    // Snap Percentage
    public decimal SnapPct3Wk { get; set; }
    public decimal SnapPct5Wk { get; set; }
    public decimal SnapPctSeason { get; set; }

    // aDOT — Average Depth of Target (AirYards / Targets)
    public decimal ADot3Wk { get; set; }
    public decimal ADot5Wk { get; set; }
    public decimal ADotSeason { get; set; }

    // TPRR — Targets Per Route Run (Targets / OffenseSnaps as route proxy)
    public decimal Tprr3Wk { get; set; }
    public decimal Tprr5Wk { get; set; }
    public decimal TprrSeason { get; set; }

    public DateTime CalculatedAt { get; set; }
    public int DataWeeksAvailable { get; set; }

    // Role classification — set by RoleClassificationService after metrics are calculated
    public PlayerRole Role { get; set; } = PlayerRole.Unknown;
    public DateTime? RoleClassifiedAt { get; set; }
}