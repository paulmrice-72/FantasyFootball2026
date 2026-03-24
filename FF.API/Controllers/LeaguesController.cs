using FF.Application.Leagues.Commands.SyncUserLeagues;
using FF.Application.Leagues.Commands.ImportLeague;
using FF.Application.Leagues.Queries.GetAllLeagues;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

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

    /// <summary>
    /// Returns all leagues currently imported into the local database.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeagues(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllLeaguesQuery(), cancellationToken);
        return Ok(result);
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
}
