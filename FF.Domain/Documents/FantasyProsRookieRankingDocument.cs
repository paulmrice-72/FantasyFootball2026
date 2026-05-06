namespace FF.Domain.Documents;

public class FantasyProsRookieRankingDocument
{
    public string Id { get; set; } = string.Empty;
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public int FantasyProsRank { get; set; }
    public int PositionRank { get; set; }
    public string? Tier { get; set; }
    public int Season { get; set; }
    public DateTime ImportedAt { get; set; }

    /// <summary>"Rookie" or "Dynasty" — distinguishes the two import types in the same collection.</summary>
    public string RankingType { get; set; } = "Rookie";
}