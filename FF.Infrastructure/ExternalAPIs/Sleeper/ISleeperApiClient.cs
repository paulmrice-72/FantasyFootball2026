// FF.Infrastructure/ExternalApis/Sleeper/ISleeperApiClient.cs
//
// This is the Refit interface. Each method here maps to one Sleeper API endpoint.
//
// HOW REFIT WORKS:
// You define the interface with attributes describing the HTTP method and route.
// Refit reads those attributes at runtime and generates a real HttpClient
// implementation automatically. You never write the HTTP boilerplate yourself.
//
// Example: [Get("/v1/players/nfl")] tells Refit to call GET https://api.sleeper.app/v1/players/nfl
// The return type Task<T> tells Refit how to deserialize the JSON response.

using FF.Infrastructure.ExternalApis.Sleeper.Dtos;
using Refit;

namespace FF.Infrastructure.ExternalApis.Sleeper;

public interface ISleeperApiClient
{
    // ── NFL State ─────────────────────────────────────────────────────────
    // Call this to find out the current week and season type.
    // Use this before any week-specific calls so you know which week to pass.

    [Get("/v1/state/nfl")]
    Task<SleeperNflStateDto> GetNflStateAsync(CancellationToken cancellationToken = default);

    // ── Players ───────────────────────────────────────────────────────────
    // Returns ALL NFL players as a dictionary: { "player_id": SleeperPlayerDto }
    // This is a ~2MB response. Call it once weekly via Hangfire, not on demand.
    // Sleeper asks that you cache this and not hammer it repeatedly.

    [Get("/v1/players/nfl")]
    Task<Dictionary<string, SleeperPlayerDto>> GetAllPlayersAsync(CancellationToken cancellationToken = default);

    // ── User ──────────────────────────────────────────────────────────────
    // Look up a Sleeper user by their username (not user_id).
    // Use this first to get the user_id needed for league lookups.

    [Get("/v1/user/{username}")]
    Task<SleeperUserDto> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);

    [Get("/v1/user/{userId}")]
    Task<SleeperUserDto> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);

    // ── Leagues ───────────────────────────────────────────────────────────
    // Get all leagues for a user in a given season.
    // sport is always "nfl" for our purposes.

    [Get("/v1/user/{userId}/leagues/nfl/{season}")]
    Task<List<SleeperLeagueDto>> GetLeaguesForUserAsync(string userId, string season, CancellationToken cancellationToken = default);

    // Get full details for a single league including scoring settings.
    // This is where you get the scoring_settings dictionary to understand
    // how points are calculated (PPR, half-PPR, etc.)

    [Get("/v1/league/{leagueId}")]
    Task<SleeperLeagueDto> GetLeagueAsync(string leagueId, CancellationToken cancellationToken = default);

    // ── Rosters ───────────────────────────────────────────────────────────
    // Get all rosters (teams) in a league.
    // Each roster has a list of player_ids on the team.
    // Match roster.OwnerId to a SleeperLeagueUserDto.UserId to get the owner's name.

    [Get("/v1/league/{leagueId}/rosters")]
    Task<List<SleeperRosterDto>> GetRostersAsync(string leagueId, CancellationToken cancellationToken = default);

    // ── Users in League ───────────────────────────────────────────────────
    // Get all users (owners) in a league.
    // The metadata.team_name field has their custom team name.

    [Get("/v1/league/{leagueId}/users")]
    Task<List<SleeperLeagueUserDto>> GetUsersInLeagueAsync(string leagueId, CancellationToken cancellationToken = default);

    // ── Transactions ──────────────────────────────────────────────────────
    // Get transactions for a league for a given waiver round (week number).
    // Covers trades, waiver claims, and free agent adds/drops.
    // Call with round = 1 through 18 (or current week) to get full history.

    [Get("/v1/league/{leagueId}/transactions/{round}")]
    Task<List<SleeperTransactionDto>> GetTransactionsAsync(string leagueId, int round, CancellationToken cancellationToken = default);

    // ── Matchups ──────────────────────────────────────────────────────────
    // Get matchup data for a specific week.
    // Returns one entry per roster. Match entries with the same matchup_id
    // to find each team's opponent for that week.

    [Get("/v1/league/{leagueId}/matchups/{week}")]
    Task<List<SleeperMatchupDto>> GetMatchupsAsync(string leagueId, int week, CancellationToken cancellationToken = default);
}