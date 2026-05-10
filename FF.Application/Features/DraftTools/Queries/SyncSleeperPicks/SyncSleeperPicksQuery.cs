// FF.Application/Features/DraftTools/Queries/SyncSleeperPicks/SyncSleeperPicksQuery.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Queries.SyncSleeperPicks;

public record SyncSleeperPicksQuery(
    string SessionId,
    string UserId) : IRequest<Result<SyncSleeperPicksResult>>;

public record SyncSleeperPicksResult(
    List<SyncedPickDto> NewPicks,
    int TotalPicksInSession,
    bool DraftComplete,
    int TotalPicksInDraft,
    List<SyncedRemainingPickDto> RemainingPicks,

    /// <summary>
    /// Only sent on first sync or when a roster trade is detected.
    /// Null on subsequent polls with no change — Blazor retains its prior value.
    /// </summary>
    Dictionary<string, int>? LiveRosterPositionCounts,

    bool MyRosterChanged);

public record SyncedPickDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    int Round,
    int Slot,
    bool IsMyPick);

public record SyncedRemainingPickDto(
    int PickNo,
    int Round,
    int Slot,
    string TeamName,
    string SleeperRosterId,
    bool IsMyPick);
