// FF.API/Controllers/DraftToolsController.cs
using FF.Application.Features.DraftTools.Commands.ImportConsensusAdp;
using FF.Application.Features.DraftTools.Commands.ImportFantasyProsDynastyRankings;
using FF.Application.Features.DraftTools.Commands.ImportFantasyProsRookeRankings;
using FF.Application.Features.DraftTools.Commands.ImportPffDraftGrades;
using FF.Application.Features.DraftTools.Commands.RecordDraftPick;
using FF.Application.Features.DraftTools.Commands.StartDraftSession;
using FF.Application.Features.DraftTools.Queries.GetDraftSession;
using FF.Application.Features.DraftTools.Queries.GetRedraftBoard;
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

    // ── FantasyPros imports (Admin only) ──────────────────────────────────

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

    /// <summary>
    /// POST /api/v1/drafttools/dynasty/import
    /// Imports FantasyPros dynasty superflex rankings CSV.
    /// Stored in same collection as rookie rankings with RankingType = "Dynasty".
    /// Used by DfvCalculationService as a floor signal for veteran players
    /// whose career sim data is stale or missing (e.g. no 2025 stats yet).
    /// CSV format: RK,TIERS,PLAYER NAME,TEAM,POS,AGE,BEST,WORST,AVG.,STD.DEV,ECR VS. ADP
    /// Source: fantasypros.com/nfl/rankings/dynasty-superflex.php → Export
    /// </summary>
    [HttpPost("dynasty/import")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ImportFantasyProsDynastyRankings(
        [FromBody] ImportFantasyProsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ImportFantasyProsDynastyRankingsCommand(request.CsvContent, request.Season),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    // ── Redraft board (preseason fallback) ───────────────────────────────
    // FIX-PRESEASON-001 (2026-08-27): before real Week-N simulation data
    // exists for the current season, merges live FFC ADP (covers rookies —
    // real 2026 drafts already have them going) with prior-season per-game
    // average for context. See GetRedraftBoardQueryHandler. Anonymous read
    // access would be reasonable here too, but kept behind [Authorize] like
    // the rest of this controller for consistency.
    [HttpGet("redraft-board")]
    public async Task<IActionResult> GetRedraftBoard(
        [FromQuery] int season,
        [FromQuery] string? position,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetRedraftBoardQuery(season, position), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    // ── Draft session ─────────────────────────────────────────────────────

    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession(
        [FromBody] StartSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

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

    [HttpGet("sessions/active")]
    public async Task<IActionResult> GetActiveSession(
        [FromQuery] string leagueId,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await mediator.Send(
            new GetActiveSessionQuery(userId, leagueId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

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
                request.NflTeam,
                request.Round,
                request.Slot,
                request.PickedByTeamName,
                request.IsMyPick),
            cancellationToken);

        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpPost("sync/draft-picks")]
    [Authorize(Roles = "Admin")]
    public IActionResult TriggerDraftPickSync([FromQuery] int season = 2026)
    {
        BackgroundJob.Enqueue<NflverseDraftPickSyncJob>(
            job => job.RunAsync(season, CancellationToken.None));

        return Ok(new { message = $"Draft pick sync queued for season {season}" });
    }

    // ── PFF Draft Grades import (Admin only) ─────────────────────────────

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

    // ── Consensus ADP import (Admin only) ────────────────────────────────

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

    // ── Request DTOs ──────────────────────────────────────────────────────
    public record StartSessionRequest(string LeagueId, string LeagueName, int Season);
    public record ImportFantasyProsRequest(string CsvContent, int Season);
    public record RecordPickRequest(
        string SleeperPlayerId,
        string PlayerName,
        string Position,
        string? NflTeam,
        int Round,
        int Slot,
        string? PickedByTeamName,
        bool IsMyPick);
    public record ImportPffRequest(string CsvContent, int Season);
    public record ImportAdpRequest(string CsvContent, int Season, string Source);
}