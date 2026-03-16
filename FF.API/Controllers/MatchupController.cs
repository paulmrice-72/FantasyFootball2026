using FF.Application.Features.Matchup;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static FF.Application.Features.Matchup.CalculateDefensiveRankingsCommandHandler;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/matchup")]
public class MatchupController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Get matchup difficulty score for a team/position/week
    /// </summary>
    [HttpGet("difficulty")]
    public async Task<IActionResult> GetDifficulty(
        [FromQuery] string team,
        [FromQuery] string position,
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(position))
            return BadRequest("team and position are required.");

        var result = await mediator.Send(
            new GetMatchupDifficultyQuery(team.ToUpper(), position.ToUpper(), season, week),
            cancellationToken);

        if (result is null)
            return NotFound($"No defensive ranking found for {team} vs {position} week {week} {season}.");

        return Ok(result);
    }

    /// <summary>
    /// Calculate defensive rankings through a given week
    /// </summary>
    [HttpPost("rankings/calculate")]
    //[Authorize]
    public async Task<IActionResult> Calculate(
        [FromQuery] int season,
        [FromQuery] int throughWeek,
        CancellationToken cancellationToken)
    {
        if (season < 2022 || throughWeek < 1 || throughWeek > 18)
            return BadRequest("Invalid season or week range.");

        var result = await mediator.Send(
            new CalculateDefensiveRankingsCommand(season, throughWeek),
            cancellationToken);

        if (!result.Success)
            return StatusCode(500, result.ErrorMessage);

        return Ok(new { message = $"Defensive rankings calculated for {season} through week {throughWeek}." });
    }

    /// <summary>
    /// Get all defensive rankings for a given season/week
    /// </summary>
    [HttpGet("rankings")]
    public async Task<IActionResult> GetRankings(
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetDefensiveRankingsQuery(season, week),
            cancellationToken);

        return Ok(result);
    }
}