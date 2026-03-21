// FF.API/Controllers/ProjectionsController.cs
using FF.Application.Features.Lineups.Commands.OptimizeLineup;
using FF.Application.Features.Projections.Commands.CalculateProjections;
using FF.Application.Features.Projections.Commands.SaveWeightProfile;
using FF.Application.Features.Projections.Queries.GetWeightProfile;
using FF.Application.Features.Simulations.Commands.RunSimulations;
using FF.Application.Interfaces.Persistence;
using FF.Application.Services.LineupOptimizer;
using FF.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FF.API.Controllers
{
    [ApiController]
    [Route("api/v1/projections")]
    // [Authorize]
    public class ProjectionsController(
    IPlayerProjectionRepository repo,
    ISimulationResultRepository simulationRepo,
    IMediator mediator) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] int season,
            [FromQuery] int week,
            [FromQuery] string? position,
            CancellationToken ct)
        {
            if (season == 0 || week == 0)
                return BadRequest("season and week are required.");

            var results = string.IsNullOrWhiteSpace(position)
                ? await repo.GetByWeekAsync(season, week, ct)
                : await repo.GetByPositionAsync(season, week, position.ToUpper(), ct);

            return Ok(results);
        }

        [HttpPost("calculate")]
        public async Task<IActionResult> Calculate(
            [FromQuery] int season,
            [FromQuery] int week,
            CancellationToken ct)
        {
            if (season == 0 || week == 0)
                return BadRequest("season and week are required.");

            var result = await mediator.Send(
                new CalculateProjectionsCommand(season, week), ct);

            return result.IsSuccess
                ? Ok(result.Value)
                : BadRequest(result.Error);
        }

        [HttpPost("simulate")]
        public async Task<IActionResult> Simulate(
    [FromQuery] int season,
    [FromQuery] int week,
    CancellationToken ct)
        {
            if (season == 0 || week == 0)
                return BadRequest("season and week are required.");

            var result = await mediator.Send(
                new RunSimulationsCommand(season, week), ct);

            return result.IsSuccess
                ? Ok(result.Value)
                : BadRequest(result.Error);
        }

        [HttpGet("simulations")]
        public async Task<IActionResult> GetSimulations(
            [FromQuery] int season,
            [FromQuery] int week,
            [FromQuery] string? position,
            CancellationToken ct)
        {
            if (season == 0 || week == 0)
                return BadRequest("season and week are required.");

            var results = string.IsNullOrWhiteSpace(position)
                ? await simulationRepo.GetByWeekAsync(season, week, ct)
                : await simulationRepo.GetByPositionAsync(season, week, position.ToUpper(), ct);

            return Ok(results);
        }

        [HttpPost("optimize")]
        public async Task<IActionResult> Optimize(
           [FromQuery] int season,
           [FromQuery] int week,
           [FromQuery] OptimizationMode mode = OptimizationMode.Median,
           [FromQuery] RiskProfile? riskProfile = null,
           [FromBody] OptimizeLineupRequest? request = null,
           CancellationToken ct = default)
        {
            if (season == 0 || week == 0)
                return BadRequest("season and week are required.");

            var result = await mediator.Send(new OptimizeLineupCommand(
                season,
                week,
                mode,
                riskProfile,
                request?.LockedPlayerIds,
                request?.ExcludedPlayerIds), ct);

            return result.IsSuccess
                ? Ok(result.Value)
                : BadRequest(result.Error);
        }

        public record OptimizeLineupRequest(
            IReadOnlyList<string>? LockedPlayerIds,
            IReadOnlyList<string>? ExcludedPlayerIds);

        [HttpGet("weight-profile")]
        public async Task<IActionResult> GetWeightProfile(CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await mediator.Send(new GetWeightProfileQuery(userId), ct);
            return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
        }

        [HttpPost("weight-profile")]
        public async Task<IActionResult> SaveWeightProfile(
            [FromBody] SaveWeightProfileRequest request,
            CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await mediator.Send(new SaveWeightProfileCommand(
                userId,
                request.ProfileName,
                request.RecentGameWeight,
                request.SnapCountWeight,
                request.MatchupWeight,
                request.MinGamesRequired,
                request.LookbackWeeks), ct);

            return result.IsSuccess ? Ok() : BadRequest(result.Error);
        }

        public record SaveWeightProfileRequest(
            string ProfileName,
            decimal RecentGameWeight,
            decimal SnapCountWeight,
            decimal MatchupWeight,
            int MinGamesRequired,
            int LookbackWeeks);
    }
}