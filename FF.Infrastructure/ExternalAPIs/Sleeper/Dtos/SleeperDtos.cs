// FF.Infrastructure/ExternalApis/Sleeper/Dtos/SleeperDtos.cs
//
// Data Transfer Objects for the Sleeper API responses.
// These map directly to what Sleeper's API returns as JSON.
// They are infrastructure concerns - they never leave FF.Infrastructure.
// Mapping to domain entities happens in the command/query handlers.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace FF.Infrastructure.ExternalApis.Sleeper.Dtos;

// ── Player ──────────────────────────────────────────────────────────────────── 
/// <summary>
/// Represents a single player entry from GET /v1/players/nfl
/// Note: Sleeper returns ALL players as a dictionary keyed by player_id
/// e.g. { "1234": { "player_id": "1234", "first_name": "Patrick", ... } }
/// </summary>
public class SleeperPlayerDto
{
    [JsonPropertyName("player_id")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }

    [JsonPropertyName("age")]
    public int? Age { get; set; }

    [JsonPropertyName("years_exp")]
    public int? YearsExp { get; set; }

    [JsonPropertyName("jersey_number")]
    public int? Number { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("injury_status")]
    public string? InjuryStatus { get; set; }

    [JsonPropertyName("sport")]
    public string? Sport { get; set; }

    [JsonPropertyName("active")]
    public bool? Active { get; set; }

    [JsonPropertyName("search_rank")]
    public int? SearchRank { get; set; }

    [JsonPropertyName("gsis_id")]
    public string? GsisId { get; set; }

    [JsonPropertyName("college")]
    public string? College { get; set; }
}

// ── User ───────────────────────────────────────────────────────────────────── 
/// <summary>
/// Represents a Sleeper user from GET /v1/user/{username}
/// </summary>
public class SleeperUserDto
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

// ── League ─────────────────────────────────────────────────────────────────── 
/// <summary>
/// Represents a Sleeper league from GET /v1/league/{league_id}
/// or from GET /v1/user/{user_id}/leagues/nfl/{season}
/// </summary>
public class SleeperLeagueDto
{
    [JsonPropertyName("league_id")]
    public string? LeagueId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("season_type")]
    public string? SeasonType { get; set; } // "regular", "pre", "post"

    [JsonPropertyName("status")]
    public string? Status { get; set; } // "pre_draft", "drafting", "in_season", "complete"

    [JsonPropertyName("sport")]
    public string? Sport { get; set; }

    [JsonPropertyName("total_rosters")]
    public int TotalRosters { get; set; }

    [JsonPropertyName("settings")]
    public SleeperLeagueSettingsDto? Settings { get; set; }

    [JsonPropertyName("scoring_settings")]
    public Dictionary<string, decimal>? ScoringSettings { get; set; }

    [JsonPropertyName("roster_positions")]
    public List<string>? RosterPositions { get; set; }

    [JsonPropertyName("previous_league_id")]
    public string? PreviousLeagueId { get; set; } // useful for dynasty leagues

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

public class SleeperLeagueSettingsDto
{
    [JsonPropertyName("playoff_teams")]
    public int PlayoffTeams { get; set; }

    [JsonPropertyName("playoff_week_start")]
    public int PlayoffWeekStart { get; set; }

    [JsonPropertyName("max_keepers")]
    public int MaxKeepers { get; set; }

    [JsonPropertyName("draft_rounds")]
    public int DraftRounds { get; set; }

    [JsonPropertyName("trade_deadline")]
    public int TradeDeadline { get; set; }

    [JsonPropertyName("waiver_type")]
    public int WaiverType { get; set; } // 0=FAAB, 1=standard, 2=rolling

    [JsonPropertyName("waiver_budget")]
    public int WaiverBudget { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; } // 0=Redraft, 1=Keeper, 2=Dynasty

    [JsonPropertyName("trade_pick_limit")]
    public int TradePickLimit { get; set; } // number of years out picks can be traded; 0 = not allowed
}

// ── Roster ─────────────────────────────────────────────────────────────────── 
/// <summary>
/// Represents a team roster from GET /v1/league/{league_id}/rosters
/// </summary>
public class SleeperRosterDto
{
    [JsonPropertyName("roster_id")]
    public int RosterId { get; set; }

    [JsonPropertyName("owner_id")]
    public string? OwnerId { get; set; }

    [JsonPropertyName("league_id")]
    public string? LeagueId { get; set; }

    [JsonPropertyName("players")]
    public List<string>? Players { get; set; } // list of player_ids

    [JsonPropertyName("starters")]
    public List<string>? Starters { get; set; } // player_ids currently starting

    [JsonPropertyName("reserve")]
    public List<string>? Reserve { get; set; } // IR slots

    [JsonPropertyName("taxi")]
    public List<string>? Taxi { get; set; } // dynasty taxi squad

    [JsonPropertyName("settings")]
    public SleeperRosterSettingsDto? Settings { get; set; }

}

public class SleeperRosterSettingsDto
{
    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }

    [JsonPropertyName("ties")]
    public int Ties { get; set; }

    [JsonPropertyName("fpts")]
    public int Fpts { get; set; } // fantasy points (integer part)

    [JsonPropertyName("fpts_decimal")]
    public int FptsDecimal { get; set; } // decimal part (e.g. 45 = .45)

    [JsonPropertyName("fpts_against")]
    public int FptsAgainst { get; set; }

    [JsonPropertyName("waiver_position")]
    public int WaiverPosition { get; set; }

    [JsonPropertyName("waiver_budget_used")]
    public int WaiverBudgetUsed { get; set; }
}

// ── League User ─────────────────────────────────────────────────────────────── 
/// <summary>
/// Represents a user within a league from GET /v1/league/{league_id}/users
/// </summary>
public class SleeperLeagueUserDto
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("metadata")]
    public SleeperLeagueUserMetadataDto? Metadata { get; set; }

    [JsonPropertyName("is_owner")]
    public bool? IsOwner { get; set; }
}

public class SleeperLeagueUserMetadataDto
{
    [JsonPropertyName("team_name")]
    public string? TeamName { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

// ── Transaction ─────────────────────────────────────────────────────────────── 
/// <summary>
/// Represents a transaction from GET /v1/league/{league_id}/transactions/{round}
/// Covers trades, waiver claims, free agent adds/drops
/// </summary>
public class SleeperTransactionDto
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; } // "trade", "waiver", "free_agent"

    [JsonPropertyName("status")]
    public string? Status { get; set; } // "complete", "failed"

    [JsonPropertyName("roster_ids")]
    public List<int>? RosterIds { get; set; }

    [JsonPropertyName("adds")]
    public Dictionary<string, int>? Adds { get; set; } // player_id -> roster_id

    [JsonPropertyName("drops")]
    public Dictionary<string, int>? Drops { get; set; } // player_id -> roster_id

    [JsonPropertyName("draft_picks")]
    public List<SleeperDraftPickDto>? DraftPicks { get; set; }

    [JsonPropertyName("waiver_budget")]
    public List<SleeperWaiverBudgetDto>? WaiverBudget { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; } // Unix timestamp in milliseconds
}

public class SleeperDraftPickDto
{
    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("round")]
    public int Round { get; set; }

    [JsonPropertyName("roster_id")]
    public int RosterId { get; set; } // current owner of pick

    [JsonPropertyName("previous_owner_id")]
    public int PreviousOwnerId { get; set; } // original owner

    [JsonPropertyName("owner_id")]
    public int OwnerId { get; set; }
}

public class SleeperWaiverBudgetDto
{
    [JsonPropertyName("sender")]
    public int Sender { get; set; }

    [JsonPropertyName("receiver")]
    public int Receiver { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

// ── Matchup ─────────────────────────────────────────────────────────────────── 
/// <summary>
/// Represents a matchup entry from GET /v1/league/{league_id}/matchups/{week}
/// Each roster has one entry; match them by matchup_id to find opponents
/// </summary>
public class SleeperMatchupDto
{
    [JsonPropertyName("matchup_id")]
    public int MatchupId { get; set; }

    [JsonPropertyName("roster_id")]
    public int RosterId { get; set; }

    [JsonPropertyName("points")]
    public decimal Points { get; set; }

    [JsonPropertyName("starters")]
    public List<string>? Starters { get; set; }

    [JsonPropertyName("players")]
    public List<string>? Players { get; set; }

    [JsonPropertyName("starters_points")]
    public List<decimal>? StartersPoints { get; set; }

    [JsonPropertyName("players_points")]
    public Dictionary<string, decimal>? PlayersPoints { get; set; }
}

// ── NFL State ───────────────────────────────────────────────────────────────── 
/// <summary>
/// Represents the current NFL state from GET /v1/state/nfl
/// Useful for knowing current week, season, and season type
/// </summary>
public class SleeperNflStateDto
{
    [JsonPropertyName("week")]
    public int Week { get; set; }

    [JsonPropertyName("season_type")]
    public string? SeasonType { get; set; } // "regular", "pre", "post"

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("display_week")]
    public int DisplayWeek { get; set; }

    [JsonPropertyName("league_season")]
    public string? LeagueSeason { get; set; }
}

// ── Draft summary ────────────────────────────────────────────────────────────
/// <summary>
/// Summary of a draft from GET /v1/league/{league_id}/drafts
/// Use draft_id to fetch picks and traded picks for that specific draft.
/// </summary>
public class SleeperLeagueDraftDto
{
    [JsonPropertyName("draft_id")]
    public string? DraftId { get; set; }

    [JsonPropertyName("league_id")]
    public string? LeagueId { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("season_type")]
    public string? SeasonType { get; set; } // "regular"

    [JsonPropertyName("status")]
    public string? Status { get; set; } // "pre_draft", "drafting", "complete"

    [JsonPropertyName("type")]
    public string? Type { get; set; } // "snake", "linear"

    [JsonPropertyName("settings")]
    public SleeperDraftSettingsDto? Settings { get; set; }

    // draft_order maps user_id → draft slot (1-based)
    [JsonPropertyName("draft_order")]
    public Dictionary<string, int>? DraftOrder { get; set; }

    // slot_to_roster_id maps draft slot (string) → roster_id
    [JsonPropertyName("slot_to_roster_id")]
    public Dictionary<string, int>? SlotToRosterId { get; set; }
}

public class SleeperDraftSettingsDto
{
    [JsonPropertyName("rounds")]
    public int Rounds { get; set; }

    [JsonPropertyName("teams")]
    public int Teams { get; set; }
}

// ── Draft pick detail ────────────────────────────────────────────────────────
/// <summary>
/// A single pick from GET /v1/draft/{draft_id}/picks
/// Contains the actual pick_no (e.g. 7 for 1.07) and draft_slot (column).
/// roster_id here is who RECEIVES the pick (current owner after trades).
/// Only available once the commissioner has set the draft order.
/// </summary>
public class SleeperDraftPickDetailDto
{
    [JsonPropertyName("draft_id")]
    public string? DraftId { get; set; }

    [JsonPropertyName("pick_no")]
    public int PickNo { get; set; } // overall pick number (1, 2, 3...)

    [JsonPropertyName("round")]
    public int Round { get; set; }

    [JsonPropertyName("draft_slot")]
    public int DraftSlot { get; set; } // column in the draft board (1-based)

    // roster_id of who will make/receive this pick (current owner)
    [JsonPropertyName("roster_id")]
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string? RosterId { get; set; }

    // picked_by = user_id (empty string if slot has no user)
    [JsonPropertyName("picked_by")]
    public string? PickedBy { get; set; }

    // player_id if the pick has been made; null for future picks
    [JsonPropertyName("player_id")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("metadata")]
    public SleeperDraftPickMetadataDto? Metadata { get; set; }

    [JsonPropertyName("is_keeper")]
    public bool? IsKeeper { get; set; }
}

public class SleeperDraftPickMetadataDto
{
    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }

    [JsonPropertyName("player_id")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class JsonStringOrNumberConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64().ToString(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => reader.GetString()
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}