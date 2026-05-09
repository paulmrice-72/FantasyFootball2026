// FF.Domain/Documents/DraftSessionDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// Tracks draft picks made during a user's dynasty rookie draft session.
/// Collection: draft_sessions
/// </summary>
public class DraftSessionDocument
{
    public string Id { get; set; } = string.Empty; // Guid string
    public string UserId { get; set; } = string.Empty;
    public string LeagueId { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public int Season { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Sleeper draft_id for the active rookie draft in this league.
    /// Populated at session start. Null if no active draft found.
    /// Used by the auto-sync polling endpoint.
    /// </summary>
    public string? SleeperDraftId { get; set; }

    /// <summary>
    /// The user's roster_id in this league, used to determine IsMyPick during auto-sync.
    /// </summary>
    public int? MyRosterId { get; set; }

    public List<DraftPick> Picks { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DraftPick
{
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int Round { get; set; }
    public int Slot { get; set; }
    public string? PickedByTeamName { get; set; }
    public bool IsMyPick { get; set; }
    public DateTime PickedAt { get; set; }
}
