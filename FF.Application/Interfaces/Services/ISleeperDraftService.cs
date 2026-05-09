// FF.Application/Interfaces/Services/ISleeperDraftService.cs
namespace FF.Application.Interfaces.Services;

/// <summary>
/// Abstracts Sleeper draft API calls for use in FF.Application.
/// Implemented in FF.Infrastructure by SleeperDraftService.
/// </summary>
public interface ISleeperDraftService
{
    /// <summary>
    /// Returns the draft_id of the most recent non-complete draft for a league,
    /// or null if no active/pre-draft draft exists.
    /// </summary>
    Task<string?> GetActiveDraftIdAsync(string leagueId, CancellationToken ct = default);

    /// <summary>
    /// Returns the roster_id for the given Sleeper user in the given league,
    /// or null if the user is not in the league.
    /// </summary>
    Task<int?> GetMyRosterIdAsync(string leagueId, string sleeperUserId, CancellationToken ct = default);

    /// <summary>
    /// Returns all picks that have been made in the given draft (player_id is non-null).
    /// </summary>
    Task<List<SleeperMadePickDto>> GetMadePicksAsync(string draftId, CancellationToken ct = default);

    /// <summary>
    /// Returns draft status ("pre_draft", "drafting", "complete") and total expected picks
    /// (rounds × teams) so the sync handler can stop polling when Sleeper says it's done.
    /// </summary>
    Task<SleeperDraftStatusDto> GetDraftStatusAsync(string draftId, CancellationToken ct = default);
}

public record SleeperDraftStatusDto(
    string Status,          // "pre_draft" | "drafting" | "complete"
    int TotalPicks);        // rounds × teams — 0 if settings unavailable

public record SleeperMadePickDto(
    string PlayerId,
    string PlayerName,
    string Position,
    int Round,
    int DraftSlot,
    string RosterId);   // roster_id as string (matches SleeperDraftPickDetailDto)