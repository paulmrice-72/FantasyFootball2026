namespace FF.Domain.Documents;

/// <summary>
/// Stores the current player assignments for one Sleeper roster.
/// One document per roster — upserted on every league sync.
/// Collection: roster_players
/// </summary>
public class RosterPlayerDocument
{
    public string Id { get; set; } = string.Empty;
    public string SleeperLeagueId { get; set; } = string.Empty;
    public string SleeperRosterId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string? SleeperUserId { get; set; }

    /// <summary>All player IDs currently on the roster (includes bench + IR).</summary>
    public List<string> PlayerIds { get; set; } = [];

    /// <summary>Player IDs in the starting lineup for the current week.</summary>
    public List<string> StarterIds { get; set; } = [];

    public int Season { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Ties { get; set; }
    public int WaiverPosition { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}