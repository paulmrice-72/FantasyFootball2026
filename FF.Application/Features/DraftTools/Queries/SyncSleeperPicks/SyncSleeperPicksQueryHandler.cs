// FF.Application/Features/DraftTools/Queries/SyncSleeperPicks/SyncSleeperPicksQueryHandler.cs
using FF.Application.Features.DraftTools.Commands.RecordDraftPick;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Queries.SyncSleeperPicks;

public class SyncSleeperPicksQueryHandler(
    IDraftSessionRepository sessionRepository,
    ISleeperDraftService sleeperDraftService,
    IPlayerRepository playerRepository,
    IMediator mediator,
    ILogger<SyncSleeperPicksQueryHandler> logger)
    : IRequestHandler<SyncSleeperPicksQuery, Result<SyncSleeperPicksResult>>
{
    public async Task<Result<SyncSleeperPicksResult>> Handle(
        SyncSleeperPicksQuery request, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);

        if (session is null)
            return Result.Failure<SyncSleeperPicksResult>(
                Error.NotFound("Draft.SessionNotFound", "Draft session not found."));

        if (session.UserId != request.UserId)
            return Result.Failure<SyncSleeperPicksResult>(
                Error.Unauthorized("Draft.NotOwner"));

        if (!session.IsActive)
            return Result.Failure<SyncSleeperPicksResult>(
                Error.Validation("Draft.SessionClosed", "This draft session is no longer active."));

        // Manual mode — no Sleeper draft linked
        if (string.IsNullOrEmpty(session.SleeperDraftId))
        {
            session.Picks ??= [];
            return Result<SyncSleeperPicksResult>.Success(new SyncSleeperPicksResult(
                NewPicks: [],
                TotalPicksInSession: session.Picks.Count,
                DraftComplete: false,
                TotalPicksInDraft: 0,
                RemainingPicks: [],
                LiveRosterPositionCounts: null,
                MyRosterChanged: false));
        }

        // ── Parallel fetch ────────────────────────────────────────────────
        // 1. Made picks (diff to find new ones)
        // 2. Draft status (total picks, completion)
        // 3. Team info map (roster_id → name, used by remaining picks + attribution)
        // 4. Live roster player ids (trade detection) — only if roster_id known

        List<SleeperMadePickDto> sleeperPicks;
        SleeperDraftStatusDto draftStatus;
        Dictionary<int, SleeperTeamInfoDto> teamInfoMap;
        List<string> liveMyPlayerIds;

        try
        {
            var madeTask = sleeperDraftService.GetMadePicksAsync(
                session.SleeperDraftId, cancellationToken);
            var statusTask = sleeperDraftService.GetDraftStatusAsync(
                session.SleeperDraftId, cancellationToken);
            var teamInfoTask = sleeperDraftService.GetTeamInfoByRosterIdAsync(
                session.LeagueId, cancellationToken);
            var rosterTask = session.MyRosterId.HasValue
                ? sleeperDraftService.GetMyRosterPlayerIdsAsync(
                    session.LeagueId, session.MyRosterId.Value, cancellationToken)
                : Task.FromResult<List<string>>([]);

            await Task.WhenAll(madeTask, statusTask, teamInfoTask, rosterTask);

            sleeperPicks = madeTask.Result;
            draftStatus = statusTask.Result;
            teamInfoMap = teamInfoTask.Result;
            liveMyPlayerIds = rosterTask.Result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Transient Sleeper fetch failure for draft {DraftId}", session.SleeperDraftId);
            return Result<SyncSleeperPicksResult>.Success(new SyncSleeperPicksResult(
                NewPicks: [],
                TotalPicksInSession: session.Picks.Count,
                DraftComplete: false,
                TotalPicksInDraft: 0,
                RemainingPicks: [],
                LiveRosterPositionCounts: null,
                MyRosterChanged: false));
        }

        bool draftComplete = draftStatus.Status == "complete";

        // ── Diff made picks ───────────────────────────────────────────────
        // recordedIds comes from the persisted session — already includes picks
        // recorded in previous syncs, so pre-session picks are not re-reported.
        // Guard against null on old Mongo documents
        session.Picks ??= [];
        session.CachedMyPlayerIds ??= [];

        var recordedIds = session.Picks.Select(p => p.SleeperPlayerId).ToHashSet();
        var newPicks = new List<SyncedPickDto>();

        foreach (var pick in sleeperPicks)
        {
            if (recordedIds.Contains(pick.PlayerId)) continue;

            bool isMyPick = session.MyRosterId.HasValue
                && pick.RosterId == session.MyRosterId.Value.ToString();

            var player = await playerRepository.GetBySleeperIdAsync(pick.PlayerId, cancellationToken);

            string? pickedByTeamName = null;
            if (int.TryParse(pick.RosterId, out var pickRosterId)
                && teamInfoMap.TryGetValue(pickRosterId, out var teamInfo))
                pickedByTeamName = teamInfo.TeamName;

            var recordResult = await mediator.Send(
                new RecordDraftPickCommand(
                    SessionId: request.SessionId,
                    UserId: request.UserId,
                    SleeperPlayerId: pick.PlayerId,
                    PlayerName: pick.PlayerName,
                    Position: pick.Position,
                    NflTeam: player?.NflTeam,
                    Round: pick.Round,
                    Slot: pick.DraftSlot,
                    PickedByTeamName: pickedByTeamName,
                    IsMyPick: isMyPick),
                cancellationToken);

            if (recordResult.IsSuccess)
            {
                newPicks.Add(new SyncedPickDto(
                    SleeperPlayerId: pick.PlayerId,
                    PlayerName: pick.PlayerName,
                    Position: pick.Position,
                    Round: pick.Round,
                    Slot: pick.DraftSlot,
                    IsMyPick: isMyPick));

                logger.LogInformation(
                    "Auto-synced pick: {Player} R{Round}S{Slot} isMyPick={IsMyPick} pickedBy={Team}",
                    pick.PlayerName, pick.Round, pick.DraftSlot, isMyPick,
                    pickedByTeamName ?? "unknown");
            }
        }

        // ── Remaining picks — correct implementation ───────────────────────
        // Built from slot_to_roster_id + draft traded picks + made pick subtraction.
        // NOT from GetDraftPicksAsync (that only returns made picks).
        var remaining = session.MyRosterId.HasValue
            ? await sleeperDraftService.GetRemainingPicksAsync(
                session.SleeperDraftId,
                session.MyRosterId.Value,
                teamInfoMap,
                cancellationToken)
            : [];

        var remainingDtos = remaining
            .Select(r => new SyncedRemainingPickDto(
                PickNo: r.PickNo,
                Round: r.Round,
                Slot: r.Slot,
                TeamName: r.TeamName,
                SleeperRosterId: r.SleeperRosterId,
                IsMyPick: r.IsMyPick))
            .ToList();

        // ── Roster position counts — always computed and returned ─────────────
        // Blazor needs these on every sync to correctly merge live roster + draft picks.
        // Building from the already-fetched liveMyPlayerIds is cheap.
        bool myRosterChanged = false;
        Dictionary<string, int>? livePositionCounts = null;

        if (liveMyPlayerIds.Count > 0)
        {
            var cachedSet = session.CachedMyPlayerIds.ToHashSet();
            var liveSet = liveMyPlayerIds.ToHashSet();

            myRosterChanged = !cachedSet.SetEquals(liveSet);

            if (myRosterChanged)
                logger.LogInformation(
                    "Roster trade detected for session {Id}: {Added} added, {Removed} removed",
                    request.SessionId,
                    liveSet.Except(cachedSet).Count(),
                    cachedSet.Except(liveSet).Count());

            // Always build position counts — Blazor merges these with draft picks each sync
            livePositionCounts = await BuildPositionCountsAsync(
                liveMyPlayerIds, playerRepository, cancellationToken);

            // Persist cache update only when something changed or it was empty
            if (myRosterChanged || session.CachedMyPlayerIds.Count == 0)
                await sessionRepository.UpdateRosterCacheAsync(
                    request.SessionId, liveMyPlayerIds, cancellationToken);
        }

        // Reload for accurate pick total (picks were added via RecordDraftPickCommand above)
        var updated = await sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);

        return Result<SyncSleeperPicksResult>.Success(new SyncSleeperPicksResult(
            NewPicks: newPicks,
            TotalPicksInSession: updated?.Picks.Count ?? session.Picks.Count + newPicks.Count,
            DraftComplete: draftComplete,
            TotalPicksInDraft: draftStatus.TotalPicks,
            RemainingPicks: remainingDtos,
            LiveRosterPositionCounts: livePositionCounts,
            MyRosterChanged: myRosterChanged));
    }

    private static async Task<Dictionary<string, int>> BuildPositionCountsAsync(
        List<string> playerIds,
        IPlayerRepository playerRepository,
        CancellationToken ct)
    {
        // Single batch query instead of N individual lookups
        var players = await playerRepository.GetBySleeperIdsAsync(playerIds, ct);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in players)
        {
            var pos = player.Position.ToString();
            counts[pos] = counts.TryGetValue(pos, out var c) ? c + 1 : 1;
        }
        return counts;
    }
}