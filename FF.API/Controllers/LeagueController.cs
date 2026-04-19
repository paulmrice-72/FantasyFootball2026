using FF.Application.Features.League.Queries.GetLeagueTeams;
using FF.Application.Features.League.Queries.GetOpponentRoster;
using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
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
        [FromServices] IRosterPlayerRepository rosterRepo,
        [FromServices] IPlayerRepository playerRepo,
        [FromServices] ISimulationResultRepository simRepo,
        [FromServices] IInjuryAlertRepository injuryRepo,
        CancellationToken ct)
    {
        var rosterDoc = await rosterRepo.GetByRosterIdAsync(rosterId, sleeperLeagueId, ct);
        if (rosterDoc is null) return NotFound();

        // Re-use depth grade logic via the existing query — swap userId for the roster owner
        var result = await mediator.Send(
            new FF.Application.Features.Team.Queries.GetPositionalDepthGradesQuery(
                rosterDoc.SleeperUserId ?? string.Empty, sleeperLeagueId, season), ct);

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
            new FF.Application.Features.Team.Queries.GetDynastyTeamGradeQuery(
                rosterDoc.SleeperUserId ?? string.Empty, sleeperLeagueId), ct);

        return result is null ? NotFound() : Ok(result);
    }
}