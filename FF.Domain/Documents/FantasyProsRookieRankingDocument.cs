// FF.Domain/Documents/FantasyProsRookieRankingDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// One-time annual import from FantasyPros rookie rankings CSV.
/// Collection: fantasyPros_rookie_rankings
/// </summary>
public class FantasyProsRookieRankingDocument
{
    public string Id { get; set; } = string.Empty;           // SleeperPlayerId (matched) or FP rank string
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public int FantasyProsRank { get; set; }                 // Overall rookie rank (1 = best)
    public int PositionRank { get; set; }                    // e.g., WR3
    public string? Tier { get; set; }                        // "Tier 1", "Tier 2" etc if FP provides
    public int Season { get; set; }                          // 2026
    public DateTime ImportedAt { get; set; }
}