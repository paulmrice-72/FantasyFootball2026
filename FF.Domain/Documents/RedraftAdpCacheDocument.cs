// FF.Domain/Documents/RedraftAdpCacheDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// Stores FFC consensus ADP data per player per season.
/// Collection: redraftAdpCache
/// Populated by SyncRedraftAdpJob. Used by ComputeRedraftScoresJob (REDRAFT-002).
/// </summary>
public class RedraftAdpCacheDocument
{
    public string Id { get; set; } = string.Empty;
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;  // FFC name — for match verification
    public string Position { get; set; } = string.Empty;
    public string? NflTeam { get; set; }
    public double Adp { get; set; }  // e.g. 4.2 (pick number, lower = better)
    public int AdpRound { get; set; }  // derived: ceil(Adp / teamCount)
    public int Season { get; set; }
    public string ScoringFormat { get; set; } = "ppr";  // ppr | half-ppr | standard
    public int TeamCount { get; set; } = 12;
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}