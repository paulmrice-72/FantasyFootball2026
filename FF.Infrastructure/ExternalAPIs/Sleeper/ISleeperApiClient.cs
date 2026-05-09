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
    [Get("/v1/state/nfl")]
    Task<SleeperNflStateDto> GetNflStateAsync(CancellationToken cancellationToken = default);

    // ── Players ─────────────────────────────────────────────────────────── 
    [Get("/v1/players/nfl")]
    Task<Dictionary<string, SleeperPlayerDto>> GetAllPlayersAsync(CancellationToken cancellationToken = default);

    // ── User ────────────────────────────────────────────────────────────── 
    [Get("/v1/user/{username}")]
    Task<SleeperUserDto> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);

    [Get("/v1/user/{userId}")]
    Task<SleeperUserDto> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);

    // ── Leagues ─────────────────────────────────────────────────────────── 
    [Get("/v1/user/{userId}/leagues/nfl/{season}")]
    Task<List<SleeperLeagueDto>> GetLeaguesForUserAsync(string userId, string season, CancellationToken cancellationToken = default);

    [Get("/v1/league/{leagueId}")]
    Task<SleeperLeagueDto> GetLeagueAsync(string leagueId, CancellationToken cancellationToken = default);

    // ── Rosters ─────────────────────────────────────────────────────────── 
    [Get("/v1/league/{leagueId}/rosters")]
    Task<List<SleeperRosterDto>> GetRostersAsync(string leagueId, CancellationToken cancellationToken = default);

    // ── Users in League ─────────────────────────────────────────────────── 
    [Get("/v1/league/{leagueId}/users")]
    Task<List<SleeperLeagueUserDto>> GetUsersInLeagueAsync(string leagueId, CancellationToken cancellationToken = default);

    // ── Transactions ────────────────────────────────────────────────────── 
    [Get("/v1/league/{leagueId}/transactions/{round}")]
    Task<List<SleeperTransactionDto>> GetTransactionsAsync(string leagueId, int round, CancellationToken cancellationToken = default);

    // ── Matchups ────────────────────────────────────────────────────────── 
    [Get("/v1/league/{leagueId}/matchups/{week}")]
    Task<List<SleeperMatchupDto>> GetMatchupsAsync(string leagueId, int week, CancellationToken cancellationToken = default);

    // ── Traded picks (league level) ──────────────────────────────────────
    // Returns all picks that have changed hands for future seasons.
    // roster_id = original owner, owner_id = current owner.
    [Get("/v1/league/{leagueId}/traded_picks")]
    Task<List<SleeperDraftPickDto>> GetTradedPicksAsync(
        string leagueId,
        CancellationToken cancellationToken = default);

    // ── Drafts ───────────────────────────────────────────────────────────
    // Returns all drafts for a league (most recent first).
    // Dynasty leagues have one startup draft + annual rookie drafts.
    [Get("/v1/league/{leagueId}/drafts")]
    Task<List<SleeperLeagueDraftDto>> GetDraftsForLeagueAsync(
        string leagueId,
        CancellationToken cancellationToken = default);

    // ── Single draft ─────────────────────────────────────────────────────
    // Returns metadata for a specific draft including status and settings.
    [Get("/v1/draft/{draftId}")]
    Task<SleeperLeagueDraftDto> GetDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default);

    // ── Draft picks ──────────────────────────────────────────────────────
    // Returns all picks in a specific draft with actual pick_no and draft_slot.
    // Used to get the real slot number (e.g. 1.07) once draft order is set.
    // Only available after the draft order has been randomized by the commissioner.
    [Get("/v1/draft/{draftId}/picks")]
    Task<List<SleeperDraftPickDetailDto>> GetDraftPicksAsync(
        string draftId,
        CancellationToken cancellationToken = default);
}