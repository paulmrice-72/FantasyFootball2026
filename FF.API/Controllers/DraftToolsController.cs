// FF.API/Controllers/DraftToolsController.cs
using FF.Application.Features.DraftTools.Commands.ImportConsensusAdp;
using FF.Application.Features.DraftTools.Commands.ImportFantasyProsRookeRankings;
using FF.Application.Features.DraftTools.Commands.ImportPffDraftGrades;
using FF.Application.Features.DraftTools.Commands.RecordDraftPick;
using FF.Application.Features.DraftTools.Commands.StartDraftSession;
using FF.Application.Features.DraftTools.Queries.GetDraftSession;
using FF.Application.Features.DraftTools.Queries.SyncSleeperPicks;
using FF.Application.Players.Queries.GetRookiePool;
using FF.Infrastructure.Identity;
using FF.Infrastructure.Jobs;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class DraftToolsController(
    IMediator mediator,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    // ── Rookie pool ───────────────────────────────────────────────────────

    [HttpGet("rookies")]
    public async Task<IActionResult> GetRookiePool(
        [FromQuery] string? position,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetRookiePoolQuery(position), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    // ── FantasyPros import (Admin only) ───────────────────────────────────

    [HttpPost("fantasyPros/import")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ImportFantasyProsRankings(
        [FromBody] ImportFantasyProsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ImportFantasyProsRookieRankingsCommand(request.CsvContent, request.Season),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    // ── Draft session ─────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/drafttools/sessions
    /// Starts a new draft session (closes any existing active session for same league).
    /// Also looks up the active Sleeper draft_id so auto-sync can work.
    /// </summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession(
        [FromBody] StartSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Look up SleeperUserId from ApplicationUser so we can map roster_id → IsMyPick
        var appUser = await userManager.FindByIdAsync(userId);
        var sleeperUserId = appUser?.SleeperUserId;

        var result = await mediator.Send(
            new StartDraftSessionCommand(
                userId,
                request.LeagueId,
                request.LeagueName,
                request.Season,
                sleeperUserId),
            cancellationToken);

        return result.IsSuccess ? Ok(new { sessionId = result.Value }) : BadRequest(result.Error);
    }

    /// <summary>
    /// GET /api/v1/drafttools/sessions/active?leagueId={leagueId}
    /// Returns the active session for the current user+league, or 404 if none.
    /// Used on page load to auto-resume an in-progress session.
    /// </summary>
    [HttpGet("sessions/active")]
    public async Task<IActionResult> GetActiveSession(
        [FromQuery] string leagueId,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Reuse the existing repository method via a query
        var result = await mediator.Send(
            new GetActiveSessionQuery(userId, leagueId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    /// <summary>
    /// GET /api/v1/drafttools/sessions/{sessionId}
    /// Returns session state including all picks made so far.
    /// </summary>
    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await mediator.Send(
            new GetDraftSessionQuery(sessionId, userId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : NotFound(result.Error);
    }

    /// <summary>
    /// GET /api/v1/drafttools/sessions/{sessionId}/sync-sleeper
    /// Polls Sleeper for new picks, diffs against session, auto-records new picks.
    /// Returns only the newly added picks so the UI can update the board.
    /// Safe to call repeatedly — idempotent.
    /// </summary>
    [HttpGet("sessions/{sessionId}/sync-sleeper")]
    public async Task<IActionResult> SyncSleeperPicks(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await mediator.Send(
            new SyncSleeperPicksQuery(sessionId, userId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>
    /// POST /api/v1/drafttools/sessions/{sessionId}/picks
    /// Records a single pick manually. Idempotent — safe to call twice for same player.
    /// </summary>
    [HttpPost("sessions/{sessionId}/picks")]
    public async Task<IActionResult> RecordPick(
        string sessionId,
        [FromBody] RecordPickRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await mediator.Send(
            new RecordDraftPickCommand(
                sessionId,
                userId,
                request.SleeperPlayerId,
                request.PlayerName,
                request.Position,
                request.Round,
                request.Slot,
                request.PickedByTeamName,
                request.IsMyPick),
            cancellationToken);

        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    /// <summary>
    /// POST /api/v1/drafttools/sync/draft-picks?season=2026
    /// Admin only. Manually triggers nflverse draft pick sync.
    /// </summary>
    [HttpPost("sync/draft-picks")]
    [Authorize(Roles = "Admin")]
    public IActionResult TriggerDraftPickSync([FromQuery] int season = 2026)
    {
        BackgroundJob.Enqueue<NflverseDraftPickSyncJob>(
            job => job.RunAsync(season, CancellationToken.None));

        return Ok(new { message = $"Draft pick sync queued for season {season}" });
    }

    // ── PFF Draft Grades import (Admin only) ────────────────────────────
    [HttpPost("pff/import")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ImportPffGrades(
        [FromBody] ImportPffRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ImportPffDraftGradesCommand(request.CsvContent, request.Season),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    // ── Consensus ADP import (Admin only) ───────────────────────────────
    [HttpPost("adp/import")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ImportConsensusAdp(
        [FromBody] ImportAdpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ImportConsensusAdpCommand(request.CsvContent, request.Season, request.Source),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    // ── Request DTOs (thin, controller-layer only) ────────────────────────────────
    public record StartSessionRequest(string LeagueId, string LeagueName, int Season);
    public record ImportFantasyProsRequest(string CsvContent, int Season);
    public record RecordPickRequest(
        string SleeperPlayerId,
        string PlayerName,
        string Position,
        int Round,
        int Slot,
        string? PickedByTeamName,
        bool IsMyPick);
    public record ImportPffRequest(string CsvContent, int Season);
    public record ImportAdpRequest(string CsvContent, int Season, string Source);
}
