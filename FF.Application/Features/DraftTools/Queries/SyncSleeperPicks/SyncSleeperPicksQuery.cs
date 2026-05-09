// FF.Application/Features/DraftTools/Queries/SyncSleeperPicks/SyncSleeperPicksQuery.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Queries.SyncSleeperPicks;

/// <summary>
/// Polls Sleeper for new picks in the active draft, diffs against the stored session,
/// records any new picks, and returns the newly added picks for the UI to update.
/// </summary>
public record SyncSleeperPicksQuery(
    string SessionId,
    string UserId) : IRequest<Result<SyncSleeperPicksResult>>;

public record SyncSleeperPicksResult(
    List<SyncedPickDto> NewPicks,
    int TotalPicksInSession,
    bool DraftComplete);

public record SyncedPickDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    int Round,
    int Slot,
    bool IsMyPick);
