// FF.Domain/Documents/DepthChartDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// One row from nflverse depth_charts.csv — stored in the depth_charts MongoDB collection.
/// Keyed on Season + Week + GsisId + DepthPosition so rows are idempotent on re-sync.
/// </summary>
public class DepthChartDocument
{
    public string Id { get; set; } = string.Empty;

    // Natural key fields
    public int Season { get; set; }
    public int Week { get; set; }
    public string GsisId { get; set; } = string.Empty;       // NFL GSIS player ID
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;     // e.g. "QB", "WR"
    public string DepthPosition { get; set; } = string.Empty; // e.g. "QB", "WR1", "WR2"
    public int DepthTeam { get; set; }                        // 1 = starter, 2 = backup, etc.
    public string FormationPosition { get; set; } = string.Empty;

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}