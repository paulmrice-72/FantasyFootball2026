// FF.API/Controllers/WaiverController.cs
using FF.Application.Features.WaiverRecommendations.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/waiver")]
[Authorize]
public class WaiverController(IMediator mediator) : ControllerBase
{
    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] string leagueId,
        [FromQuery] int season,
        [FromQuery] int week,
        [FromQuery] string? position = null,
        [FromQuery] int top = 30,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetWaiverRecommendationsQuery(leagueId, season, week, position, top), ct);
        return Ok(result);
    }
}