// FF.Domain/Documents/DraftSessionDocument.cs
namespace FF.Domain.Documents;

public class DraftSessionDocument
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string LeagueId { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public int Season { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SleeperDraftId { get; set; }
    public int? MyRosterId { get; set; }

    /// <summary>
    /// Snapshot of the user's Sleeper player_ids at last sync.
    /// Persisted to Mongo — used to detect mid-draft roster trades.
    /// </summary>
    public List<string> CachedMyPlayerIds { get; set; } = [];

    public List<DraftPick> Picks { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DraftPick
{
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string? NflTeam { get; set; }
    public int Round { get; set; }
    public int Slot { get; set; }
    public string? PickedByTeamName { get; set; }
    public bool IsMyPick { get; set; }
    public DateTime PickedAt { get; set; }
}
