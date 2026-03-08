using FF.Application.Players.Queries.GetAllPlayers;
using FF.Application.Players.Commands.SyncPlayers;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class PlayersController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllPlayersQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Triggers an on-demand sync of the full Sleeper player universe.
    /// Fetches ~5,000+ players from Sleeper and upserts into local DB.
    /// Safe to call multiple times. Takes 10-30 seconds to complete.
    /// Also runs automatically every Tuesday at 6am via Hangfire.
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncPlayersResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SyncPlayers(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SyncPlayersCommand(), cancellationToken);

        if (!result.IsSuccess)
            return StatusCode(500, result.Error?.Message);

        return Ok(result.Value);
    }


}