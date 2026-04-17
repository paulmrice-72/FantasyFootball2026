// FF.Domain/Documents/ConsensusAdpDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// Consensus rookie ADP from NFFC, Underdog, or similar platforms.
/// Collection: consensus_adp
/// ADP is stored raw (pick number); normalized to 0-100 in the calculator.
/// </summary>
public class ConsensusAdpDocument
{
    public string Id { get; set; } = string.Empty;           // SleeperPlayerId (matched) or "unmatched-{rank}"
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public double Adp { get; set; }                          // Raw ADP pick number (1.0 = best)
    public int? AdpRank { get; set; }                        // Integer rank derived from ADP
    public string? Source { get; set; }                      // "NFFC", "Underdog", "Sleeper" etc
    public int Season { get; set; }
    public DateTime ImportedAt { get; set; }
}