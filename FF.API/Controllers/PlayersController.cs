// FF.API/Controllers/PlayersController.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Players.Commands.BackfillCollegeTeam;
using FF.Application.Players.Commands.SyncPlayers;
using FF.Application.Players.Queries.GetAllPlayers;
using FF.Application.Players.Queries.GetPlayerNarrative;
using FF.Application.Services;
using FF.Domain.Documents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class PlayersController(
    IMediator mediator,
    IPlayerRepository playerRepository,          // ← already used in /bio, just confirm it's there
    ISimulationResultRepository simulationRepo,
    IPlayerProjectionRepository projectionRepo,
    IPlayerUsageMetricsRepository usageMetricsRepo,
    IDepthChartRepository depthChartRepository,
    IDynastyValuationRepository dynastyValuationRepo) : ControllerBase
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
        if (!result.IsSuccess) return StatusCode(500, result.Error?.Message);
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
        if (player is null) return NotFound($"Player {sleeperPlayerId} not found.");

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
    /// Returns dynasty valuation (TradeValue, BreakoutScore, CareerValue, phase)
    /// for a single player. Used by PlayerCardDialog veteran breakdown.
    /// </summary>
    [HttpGet("{sleeperPlayerId}/dynasty-value")]
    public async Task<IActionResult> GetDynastyValue(
        string sleeperPlayerId,
        [FromQuery] int season = 2026,
        CancellationToken ct = default)
    {
        var val = await dynastyValuationRepo.GetBySleeperIdAsync(sleeperPlayerId, ct);
        if (val is null) return Ok(new { found = false });

        // Load depth chart to compute penalty
        var depthRows = await depthChartRepository.GetByPlayerAsync(sleeperPlayerId, season, ct);
        var depthDoc = depthRows.FirstOrDefault();

        double depthPenalty = 1.0;
        int depthSlot = depthDoc?.DepthTeam ?? 1;

        if (depthDoc is not null && (val.Position == "TE" || val.Position == "RB"))
        {
            // Build minimal lookups for the penalty helper
            var singleDepthLookup = new Dictionary<string, DepthChartDocument>
            { [sleeperPlayerId] = depthDoc };

            // Age gate: look up the TE1 for this team
            var te1AgeByTeam = new Dictionary<string, int?>();
            if (val.Position == "TE" && depthSlot >= 2)
            {
                var teamRows = await depthChartRepository
                    .GetByTeamAsync(depthDoc.NflTeam, season, depthDoc.Week, ct);
                var te1Row = teamRows.FirstOrDefault(r => r.Position == "TE" && r.DepthTeam == 1);
                if (te1Row is not null)
                {
                    var te1Player = await playerRepository.GetBySleeperIdAsync(te1Row.SleeperPlayerId, ct);
                    te1AgeByTeam[depthDoc.NflTeam] = te1Player?.Age;
                }
            }

            depthPenalty = DepthPenaltyCalculator.ComputeDepthPenalty(
                sleeperPlayerId, val.Position, singleDepthLookup, te1AgeByTeam);
        }

        var adjustedTradeValue = Math.Round(val.TradeValue * depthPenalty, 1);
        var isDepthPenalised = depthPenalty < 1.0;

        return Ok(new
        {
            found = true,
            tradeValue = val.TradeValue,
            adjustedTradeValue,
            depthPenaltyMultiplier = Math.Round(depthPenalty, 2),
            depthSlot,
            isDepthPenalised,
            breakoutScore = val.BreakoutScore,
            careerValueScore = val.CareerValueScore,
            yearsOfPrimeRemaining = val.YearsOfPrimeRemaining,
            careerPhase = val.CareerPhase.ToString(),
            yearsExperience = val.YearsExperience
        });
    }

    /// <summary>
    /// Returns (or generates) an AI scouting narrative for a player.
    /// Cached in MongoDB for 7 days.
    /// </summary>
    [HttpGet("{sleeperPlayerId}/narrative")]
    public async Task<IActionResult> GetNarrative(
        string sleeperPlayerId,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new GetPlayerNarrativeQuery(sleeperPlayerId), ct);
        if (!result.IsSuccess) return NotFound(result.Error?.Message);
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
        if (season == 0 || week == 0) return BadRequest("season and week are required.");
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
        if (season == 0 || week == 0) return BadRequest("season and week are required.");
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
        if (season == 0) return BadRequest("season is required.");
        var result = await usageMetricsRepo.GetByPlayerIdAsync(playerId, season, ct);
        if (result is null)
            return NotFound($"No usage metrics found for player {playerId} season {season}.");
        return Ok(result);
    }

    [HttpPost("backfill-college")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BackfillCollegeTeam(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("No file uploaded.");
        using var reader = new StreamReader(file.OpenReadStream());
        var csv = await reader.ReadToEndAsync(ct);
        var result = await mediator.Send(new BackfillCollegeTeamCommand(csv), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error.Message);
    }

    [HttpGet("{sleeperPlayerId}/depth-chart")]
    public async Task<IActionResult> GetDepthChart(
        string sleeperPlayerId,
        [FromQuery] int season = 2026,
        CancellationToken ct = default)
    {
        var rows = await depthChartRepository.GetByPlayerAsync(sleeperPlayerId, season, ct);
        if (rows.Count == 0)
            return Ok(new { available = false, message = "Depth chart sync has not run yet for this season." });

        var latest = rows.First();
        return Ok(new
        {
            available = true,
            week = latest.Week,
            nflTeam = latest.NflTeam,
            position = latest.Position,
            depthPosition = latest.DepthPosition,
            depthTeam = latest.DepthTeam,
            formationPosition = latest.FormationPosition
        });
    }
}