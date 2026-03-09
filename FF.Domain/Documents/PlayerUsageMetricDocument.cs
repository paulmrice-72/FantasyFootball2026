// FF.Domain/Documents/PlayerUsageMetricsDocument.cs
namespace FF.Domain.Documents;

public class PlayerUsageMetricsDocument
{
    public string? Id { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public int Season { get; set; }
    public string Position { get; set; } = string.Empty;

    // Target Share rolling averages
    public decimal TargetShare3Wk { get; set; }
    public decimal TargetShare5Wk { get; set; }
    public decimal TargetShareSeason { get; set; }

    // Snap % rolling averages
    // NOTE: SnapPct is not on PlayerGameLogDocument yet — see note below
    public decimal SnapPct3Wk { get; set; }
    public decimal SnapPct5Wk { get; set; }
    public decimal SnapPctSeason { get; set; }

    // Air Yards Share rolling averages
    public decimal AirYardsShare3Wk { get; set; }
    public decimal AirYardsShare5Wk { get; set; }
    public decimal AirYardsShareSeason { get; set; }

    // Carry Share rolling averages (RB primary, others near zero)
    public decimal CarryShare3Wk { get; set; }
    public decimal CarryShare5Wk { get; set; }
    public decimal CarryShareSeason { get; set; }

    // WOPR rolling averages — averaged from existing Wopr on game logs
    public decimal Wopr3Wk { get; set; }
    public decimal WoprSeason { get; set; }

    public int WeeksPlayed { get; set; }
    public int LastWeekProcessed { get; set; }
    public DateTime LastUpdated { get; set; }
}