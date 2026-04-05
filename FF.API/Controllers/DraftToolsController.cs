// FF.API/Controllers/DraftToolsController.cs
using FF.Application.Features.DraftTools.Commands.ImportFantasyProsRookeRankings;
using FF.Application.Features.DraftTools.Commands.RecordDraftPick;
using FF.Application.Features.DraftTools.Commands.StartDraftSession;
using FF.Application.Features.DraftTools.Queries.GetDraftSession;
using FF.Application.Players.Queries.GetRookiePool;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class DraftToolsController(IMediator mediator) : ControllerBase
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
    /// </summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession(
        [FromBody] StartSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await mediator.Send(
            new StartDraftSessionCommand(
                userId,
                request.LeagueId,
                request.LeagueName,
                request.Season),
            cancellationToken);

        return result.IsSuccess ? Ok(new { sessionId = result.Value }) : BadRequest(result.Error);
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
    /// POST /api/v1/drafttools/sessions/{sessionId}/picks
    /// Records a single pick. Idempotent — safe to call twice for same player.
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