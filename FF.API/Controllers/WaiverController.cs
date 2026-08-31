// FF.API/Controllers/WaiverController.cs
using FF.Application.Features.RosterAwareRecommendations.Queries;
using FF.Application.Features.WaiverRecommendations.Queries.GetWaiverRecommendations;
using FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;
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

    [HttpGet("recommendations/roster-aware")]
    public async Task<IActionResult> GetRosterAwareRecommendations(
    [FromQuery] string leagueId,
    [FromQuery] string sleeperUserId,
    [FromQuery] int season,
    [FromQuery] int week,
    [FromQuery] string? position = null,
    [FromQuery] int top = 30,
    CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetRosterAwareRecommendationsQuery(
                leagueId, sleeperUserId, season, week, position, top), ct);
        return Ok(result);
    }

    [HttpGet("available/offseason")]
    public async Task<IActionResult> GetOffSeasonAvailable(
    [FromQuery] string leagueId,
    [FromQuery] string? position = null,
    [FromQuery] int top = 50,
    CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetOffSeasonAvailablePlayersQuery(leagueId, position, top), ct);
        return Ok(result);
    }

    // FAN-113 (2026-08-30): pre-season counterpart to recommendations/roster-aware,
    // for the same reason available/offseason exists — no VORP data yet.
    [HttpGet("recommendations/roster-aware/offseason")]
    public async Task<IActionResult> GetOffSeasonRosterAwareRecommendations(
    [FromQuery] string leagueId,
    [FromQuery] string sleeperUserId,
    [FromQuery] string? position = null,
    [FromQuery] int top = 30,
    CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetOffSeasonRosterAwareRecommendationsQuery(leagueId, sleeperUserId, position, top), ct);
        return Ok(result);
    }
}