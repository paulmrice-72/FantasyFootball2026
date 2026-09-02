// FF.API/Controllers/WaiverController.cs
using FF.Application.Features.RosterAwareRecommendations.Queries;
using FF.Application.Features.Vorp.Commands.CalculateVorp;
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

    /// <summary>
    /// FAN-118 — computes and stores the VORP board for one league and week.
    ///
    /// Separated from the GET above, which used to do this on every read. VORP is
    /// league-scoped: both baselines depend on this league's roster configuration
    /// and its rostered set, so it has to be computed per league, not globally.
    /// </summary>
    [HttpPost("vorp/calculate")]
    public async Task<IActionResult> CalculateVorp(
        [FromQuery] string leagueId,
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leagueId))
            return BadRequest(new { error = "leagueId is required." });

        // Week 0 is the preseason sentinel and a legitimate value here — do not add
        // a `week <= 0` guard, which is the bug FAN-126 fixed on ProjectionsController.
        if (week < 0)
            return BadRequest(new { error = "week must be 0 or greater." });

        var result = await mediator.Send(
            new CalculateVorpCommand(leagueId, season, week), ct);

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