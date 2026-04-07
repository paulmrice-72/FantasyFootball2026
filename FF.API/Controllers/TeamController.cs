using FF.Application.Features.Team.Queries;
using FF.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/team")]
[Authorize]
public class TeamController(IMediator mediator, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("roster")]
    public async Task<IActionResult> GetMyRoster(
        [FromQuery] string sleeperLeagueId,
        CancellationToken ct)
    {
        var internalUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                             ?? string.Empty;

        var appUser = await userManager.FindByIdAsync(internalUserId);

        if (appUser?.SleeperUserId is null)
            return BadRequest("Sleeper account not linked.");

        if (string.IsNullOrEmpty(sleeperLeagueId))
            return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetMyRosterQuery(appUser.SleeperUserId, sleeperLeagueId), ct);

        return result is null ? NotFound("Roster not found for this league.") : Ok(result);
    }
}