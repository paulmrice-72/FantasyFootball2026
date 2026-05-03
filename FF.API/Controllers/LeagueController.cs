// FF.API/Controllers/LeagueController.cs
using FF.Application.Features.League.Queries.GetLeagueTeams;
using FF.Application.Features.League.Queries.GetOpponentRoster;
using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/league")]
[Authorize]
public class LeagueController(IMediator mediator) : ControllerBase
{
    /// <summary>Returns lightweight team list for the league picker dropdown.</summary>
    [HttpGet("teams")]
    public async Task<IActionResult> GetLeagueTeams(
        [FromQuery] string sleeperLeagueId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sleeperLeagueId))
            return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(new GetLeagueTeamsQuery(sleeperLeagueId), ct);
        return Ok(result);
    }

    /// <summary>Returns full roster for a specific opponent team.</summary>
    [HttpGet("teams/{rosterId}/roster")]
    public async Task<IActionResult> GetOpponentRoster(
        string rosterId,
        [FromQuery] string sleeperLeagueId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sleeperLeagueId))
            return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetOpponentRosterQuery(rosterId, sleeperLeagueId), ct);

        return result is null ? NotFound("Roster not found.") : Ok(result);
    }

    /// <summary>Returns depth grades for a specific opponent team.</summary>
    [HttpGet("teams/{rosterId}/depth-grades")]
    public async Task<IActionResult> GetOpponentDepthGrades(
        string rosterId,
        [FromQuery] string sleeperLeagueId,
        [FromQuery] int season,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sleeperLeagueId))
            return BadRequest("sleeperLeagueId is required.");

        // Pass SleeperRosterId directly — handler uses GetByRosterIdAsync,
        // avoiding null-userId failure for unmanaged/unclaimed Sleeper teams.
        var result = await mediator.Send(
            new GetPositionalDepthGradesQuery(
                SleeperUserId: string.Empty,
                SleeperLeagueId: sleeperLeagueId,
                Season: season,
                SleeperRosterId: rosterId), ct);

        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Returns dynasty grade for a specific opponent team.</summary>
    [HttpGet("teams/{rosterId}/dynasty-grade")]
    public async Task<IActionResult> GetOpponentDynastyGrade(
        string rosterId,
        [FromQuery] string sleeperLeagueId,
        [FromServices] IRosterPlayerRepository rosterRepo,
        CancellationToken ct)
    {
        var rosterDoc = await rosterRepo.GetByRosterIdAsync(rosterId, sleeperLeagueId, ct);
        if (rosterDoc is null) return NotFound();

        var result = await mediator.Send(
            new GetDynastyTeamGradeQuery(
                rosterDoc.SleeperUserId ?? string.Empty, sleeperLeagueId), ct);

        return result is null ? NotFound() : Ok(result);
    }
}