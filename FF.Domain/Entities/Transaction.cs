// FF.Domain/Entities/Transaction.cs
//
// Represents a fantasy football transaction — a trade, waiver claim,
// free agent add/drop, or commissioner action.
//
// The adds/drops are stored as JSON-serialized dictionaries:
//   adds:  { "player_id": roster_id_that_received_player }
//   drops: { "player_id": roster_id_that_dropped_player }
// We store the raw JSON so we don't need a separate join table,
// and can query it later when we build the analytics engine.

using System.Text.Json;
using FF.SharedKernel;

namespace FF.Domain.Entities;

public class Transaction : Entity
{
    // Foreign key to League
    public Guid LeagueId { get; private set; }

    // The unique ID from Sleeper — used for idempotent upserts
    public string SleeperTransactionId { get; private set; } = string.Empty;

    // "trade", "waiver", "free_agent", "commissioner"
    public string Type { get; private set; } = string.Empty;

    // "complete", "failed"
    public string Status { get; private set; } = string.Empty;

    // When the transaction happened on Sleeper
    public DateTime TransactionDate { get; private set; }

    // Which week of the season this transaction occurred
    public int Week { get; private set; }

    // JSON: { "player_id": roster_id } for adds
    public string? AddsJson { get; private set; }

    // JSON: { "player_id": roster_id } for drops
    public string? DropsJson { get; private set; }

    // Navigation property
    public League? League { get; private set; }

    // EF Core needs a parameterless constructor
    private Transaction() { }

    public static Transaction Create(
        Guid leagueId,
        string sleeperTransactionId,
        string type,
        string status,
        DateTime createdAt,
        int week,
        Dictionary<string, int>? adds = null,
        Dictionary<string, int>? drops = null)
    {
        return new Transaction
        {
            LeagueId = leagueId,
            SleeperTransactionId = sleeperTransactionId,
            Type = type,
            Status = status,
            TransactionDate = createdAt,
            Week = week,
            AddsJson = adds is not null
                ? JsonSerializer.Serialize(adds)
                : null,
            DropsJson = drops is not null
                ? JsonSerializer.Serialize(drops)
                : null
        };
    }

    // Helper to read adds back as a dictionary without exposing raw JSON
    public Dictionary<string, int> GetAdds() =>
        AddsJson is null
            ? new Dictionary<string, int>()
            : JsonSerializer.Deserialize<Dictionary<string, int>>(AddsJson)
              ?? new Dictionary<string, int>();

    public Dictionary<string, int> GetDrops() =>
        DropsJson is null
            ? new Dictionary<string, int>()
            : JsonSerializer.Deserialize<Dictionary<string, int>>(DropsJson)
              ?? new Dictionary<string, int>();
}
