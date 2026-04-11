// FF.API/Controllers/PlayersController.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Players.Commands.BackfillCollegeTeam;
using FF.Application.Players.Commands.SyncPlayers;
using FF.Application.Players.Queries.GetAllPlayers;
using FF.Application.Players.Queries.GetPlayerNarrative;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class PlayersController(
    IMediator mediator,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepo,
    IPlayerProjectionRepository projectionRepo,
    IPlayerUsageMetricsRepository usageMetricsRepo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAllPlayersQuery(), ct);
        return Ok(result);
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncPlayersResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SyncPlayers(CancellationToken ct)
    {
        var result = await mediator.Send(new SyncPlayersCommand(), ct);
        if (!result.IsSuccess)
            return StatusCode(500, result.Error?.Message);
        return Ok(result.Value);
    }

    /// <summary>
    /// Returns lightweight bio for the player header — headshot, name, position,
    /// team, age, jersey, college, years experience.
    /// </summary>
    [HttpGet("{sleeperPlayerId}/bio")]
    public async Task<IActionResult> GetBio(
        string sleeperPlayerId,
        CancellationToken ct)
    {
        var player = await playerRepository.GetBySleeperIdAsync(sleeperPlayerId, ct);
        if (player is null)
            return NotFound($"Player {sleeperPlayerId} not found.");

        return Ok(new
        {
            sleeperPlayerId = player.SleeperPlayerId,
            fullName = player.FullName,
            position = player.Position.ToString(),
            nflTeam = player.NflTeam,
            age = player.Age,
            jerseyNumber = player.JerseyNumber,
            collegeTeam = player.CollegeTeam,
            yearsExperience = player.YearsExperience,
            headshotUrl = player.SleeperPlayerId is not null
                               ? $"https://sleepercdn.com/content/nfl/players/thumb/{player.SleeperPlayerId}.jpg"
                               : null
        });
    }

    /// <summary>
    /// Returns (or generates) an AI scouting narrative for a rookie player.
    /// Cached in MongoDB for 7 days.
    /// </summary>
    [HttpGet("{sleeperPlayerId}/narrative")]
    public async Task<IActionResult> GetNarrative(
        string sleeperPlayerId,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new GetPlayerNarrativeQuery(sleeperPlayerId), ct);

        if (!result.IsSuccess)
            return NotFound(result.Error?.Message);

        return Ok(new { narrative = result.Value!.Narrative });
    }

    /// <summary>
    /// Returns simulation result (floor/median/ceiling/boom/bust) for one player/week.
    /// </summary>
    [HttpGet("{playerId}/simulation")]
    public async Task<IActionResult> GetSimulation(
        string playerId,
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct)
    {
        if (season == 0 || week == 0)
            return BadRequest("season and week are required.");

        var result = await simulationRepo.GetByPlayerAsync(playerId, season, week, ct);
        if (result is null)
            return NotFound($"No simulation found for player {playerId} season {season} week {week}.");

        return Ok(result);
    }

    /// <summary>
    /// Returns projection (regression model output) for one player/week.
    /// </summary>
    [HttpGet("{playerId}/projection")]
    public async Task<IActionResult> GetProjection(
        string playerId,
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct)
    {
        if (season == 0 || week == 0)
            return BadRequest("season and week are required.");

        var result = await projectionRepo.GetByPlayerAsync(playerId, season, week, ct);
        if (result is null)
            return NotFound($"No projection found for player {playerId} season {season} week {week}.");

        return Ok(result);
    }

    /// <summary>
    /// Returns usage metrics (snap%, target share, WOPR, aDOT, TPRR, role) for one player/season.
    /// </summary>
    [HttpGet("{playerId}/usage")]
    public async Task<IActionResult> GetUsage(
        string playerId,
        [FromQuery] int season,
        CancellationToken ct)
    {
        if (season == 0)
            return BadRequest("season is required.");

        var result = await usageMetricsRepo.GetByPlayerIdAsync(playerId, season, ct);
        if (result is null)
            return NotFound($"No usage metrics found for player {playerId} season {season}.");

        return Ok(result);
    }

    // POST /api/v1/players/backfill-college
    // One-shot admin endpoint — upload nflverse roster CSV to backfill CollegeTeam
    [HttpPost("backfill-college")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BackfillCollegeTeam(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file uploaded.");

        using var reader = new StreamReader(file.OpenReadStream());
        var csv = await reader.ReadToEndAsync(ct);

        var result = await mediator.Send(
            new BackfillCollegeTeamCommand(csv), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(result.Error.Message);
    }
}