// FF.Domain/Documents/CombineResultDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// Stores NFL combine measurements for a player-season.
/// Source: nflverse combine.csv — synced annually each March.
/// Key: SleeperPlayerId + Season (composite identity).
/// </summary>
public class CombineResultDocument
{
    public string Id { get; set; } = string.Empty;          // "{SleeperPlayerId}_{Season}"
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;  // as it appears in nflverse
    public string Position { get; set; } = string.Empty;
    public string? NflTeam { get; set; }
    public string? School { get; set; }
    public int Season { get; set; }                         // draft year

    // ── Raw measurements ──────────────────────────────────────────────────
    public double? HeightInches { get; set; }               // parsed from "6-2" → 74.0
    public double? WeightLbs { get; set; }
    public double? FortyYard { get; set; }                  // seconds
    public int? BenchReps { get; set; }                     // 225lb reps
    public double? Vertical { get; set; }                   // inches
    public double? BroadJump { get; set; }                  // inches
    public double? ConeDrill { get; set; }                  // seconds (3-cone)
    public double? Shuttle { get; set; }                    // seconds (20-yd shuttle)

    // ── Derived ───────────────────────────────────────────────────────────
    public double? SpeedScore { get; set; }                 // RB only: (wt×200)/forty²
    public double AthleticismScore { get; set; }            // 0-100 composite
    public string? BirthDate { get; set; }                  // "YYYY-MM-DD" from nflverse

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}