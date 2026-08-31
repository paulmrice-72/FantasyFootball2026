using FF.Application.Features.Leagues.Queries.GetLeagueRosterGrades;
using FF.Application.Features.Leagues.Queries.GetRedraftRosterGrades;
using FF.Application.Features.Leagues.Queries.GetLeagueStandings;
using FF.Application.Leagues.Commands.SetLeagueVisibility;
using FF.Application.Features.Leagues.Commands.SyncUserLeagues;
using FF.Application.Features.Leagues.Queries.GetAllLeagues;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using FF.Application.Features.Leagues.Commands.ImportLeague;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class LeaguesController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator;

    /// <summary>
    /// Imports a Sleeper league by its league ID.
    /// Safe to call multiple times — all writes are idempotent.
    /// Imports league settings, rosters, owners, and 2 seasons of transactions.
    /// </summary>
    /// <param name="leagueId">The Sleeper league ID (found in the Sleeper app URL)</param>
    [HttpPost("import/{leagueId}")]
    [ProducesResponseType(typeof(ImportLeagueResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ImportLeague(
        string leagueId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leagueId))
            return BadRequest("League ID is required");

        var result = await _mediator.Send(
            new ImportLeagueCommand(leagueId),
            cancellationToken);

        if (!result.IsSuccess)
            return StatusCode(500, result.Error?.Message);

        return Ok(result.Value);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetLeagues(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _mediator.Send(new GetAllLeaguesQuery(userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>
    /// Syncs all leagues for the currently authenticated user.
    /// Called automatically after Sleeper account linking.
    /// </summary>
    [HttpPost("sync")]
    [Authorize]
    public async Task<IActionResult> SyncMyLeagues(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var result = await _mediator.Send(
            new SyncUserLeaguesCommand(userId), cancellationToken);

        if (!result.IsSuccess)
            return StatusCode(500, result.Error?.Message);

        return Ok(result.Value);
    }

    /// <summary>Sets league visibility preference for the authenticated user.</summary>
    [HttpPost("{leagueId:guid}/visibility")]
    [Authorize]
    public async Task<IActionResult> SetVisibility(
        Guid leagueId,
        [FromBody] SetLeagueVisibilityRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var result = await _mediator.Send(
            new SetLeagueVisibilityCommand(userId, leagueId, request.IsHidden), ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    /// <summary>Returns standings for a league — all teams ranked by record.</summary>
    [HttpGet("{sleeperLeagueId}/standings")]
    [Authorize]
    public async Task<IActionResult> GetStandings(
        string sleeperLeagueId,
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sleeperLeagueId))
            return BadRequest("sleeperLeagueId is required.");

        var result = await _mediator.Send(
            new GetLeagueStandingsQuery(sleeperLeagueId, season, week), ct);

        return result is null
            ? NotFound("No roster data found for this league.")
            : Ok(result);
    }

    /// <summary>Returns dynasty-style roster grades for a league (DynastyGrade, TeamProfile, DraftCapitalScore, etc).</summary>
    [HttpGet("{sleeperLeagueId}/roster-grades")]
    public async Task<IActionResult> GetRosterGrades(
    string sleeperLeagueId,
    [FromQuery] int season,
    CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetLeagueRosterGradesQuery(sleeperLeagueId, season), ct);

        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// FAN-107: redraft-specific roster grades — Depth Score/Grade + Rank
    /// only, no dynasty concepts (DynastyScore, TeamProfile, DraftCapital).
    /// </summary>
    [HttpGet("{sleeperLeagueId}/redraft-roster-grades")]
    public async Task<IActionResult> GetRedraftRosterGrades(
        string sleeperLeagueId,
        [FromQuery] int season,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetRedraftRosterGradesQuery(sleeperLeagueId, season), ct);

        return result is null ? NotFound() : Ok(result);
    }

    public record SetLeagueVisibilityRequest(bool IsHidden);
}
