// FF.Application/Interfaces/Services/ISleeperDraftService.cs
namespace FF.Application.Interfaces.Services;

public interface ISleeperDraftService
{
    Task<string?> GetActiveDraftIdAsync(string leagueId, CancellationToken ct = default);

    Task<int?> GetMyRosterIdAsync(string leagueId, string sleeperUserId, CancellationToken ct = default);

    Task<List<SleeperMadePickDto>> GetMadePicksAsync(string draftId, CancellationToken ct = default);

    Task<SleeperDraftStatusDto> GetDraftStatusAsync(string draftId, CancellationToken ct = default);

    /// <summary>
    /// Builds the ordered list of remaining (unmade) pick slots for the active draft.
    ///
    /// How it works:
    ///   1. Fetch draft metadata (slot_to_roster_id, rounds, teams, type=snake/linear)
    ///   2. Fetch per-draft traded picks (/v1/draft/{id}/traded_picks) — overrides
    ///      slot ownership for picks that changed hands mid-draft
    ///   3. Fetch already-made picks to subtract from the full slot list
    ///   4. Generate all slots in draft order, apply trade overrides, remove made picks
    ///
    /// Result is ordered by pick_no ascending — next pick first.
    /// </summary>
    Task<List<SleeperRemainingPickDto>> GetRemainingPicksAsync(
        string draftId,
        int myRosterId,
        Dictionary<int, SleeperTeamInfoDto> teamInfoMap,
        CancellationToken ct = default);

    /// <summary>
    /// Builds roster_id → team info for all teams in the league.
    /// Joins rosters + league users on owner_id.
    /// </summary>
    Task<Dictionary<int, SleeperTeamInfoDto>> GetTeamInfoByRosterIdAsync(
        string leagueId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns current player_ids on the user's Sleeper roster (live call).
    /// Used to detect mid-draft player trades.
    /// </summary>
    Task<List<string>> GetMyRosterPlayerIdsAsync(
        string leagueId,
        int myRosterId,
        CancellationToken ct = default);
}

public record SleeperDraftStatusDto(
    string Status,
    int TotalPicks);

public record SleeperMadePickDto(
    string PlayerId,
    string PlayerName,
    string Position,
    int Round,
    int DraftSlot,
    string RosterId);

public record SleeperRemainingPickDto(
    int PickNo,
    int Round,
    int Slot,
    int RosterId,
    string TeamName,
    string SleeperRosterId,
    bool IsMyPick);

public record SleeperTeamInfoDto(
    int RosterId,
    string TeamName,
    string OwnerName,
    string SleeperRosterId);
