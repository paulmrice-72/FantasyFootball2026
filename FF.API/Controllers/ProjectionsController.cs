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
        /// <summary>
        /// Highest real NFL week. Week 0 is NOT invalid — it is the season-average
        /// sentinel written by SeedSeasonAverageSims and by preseason projection
        /// runs, and it is the only week that exists before Week 1 is played.
        /// </summary>
        private const int MinWeek = 0;
        private const int MaxWeek = 22;

        /// <summary>
        /// Validates season/week for every endpoint on this controller.
        ///
        /// Every one of these endpoints previously guarded with
        /// <c>if (season == 0 || week == 0) return BadRequest(...)</c>, which
        /// conflates "not supplied" with "zero". Week 0 is a legitimate, meaningful
        /// value — it is the preseason / season-average projection — so asking for
        /// it returned 400 and the League page rendered "Projected Players 0" while
        /// 950 Week-0 simulation rows sat in the database. Nullable parameters
        /// separate absence from zero properly.
        /// </summary>
        private static string? ValidateSeasonWeek(int? season, int? week)
        {
            if (season is null or <= 0)
                return "season is required and must be a four-digit year.";

            if (week is null)
                return "week is required. Use week=0 for the preseason / season-average projection.";

            if (week < MinWeek || week > MaxWeek)
                return $"week must be between {MinWeek} and {MaxWeek}. " +
                       "Week 0 is the preseason / season-average projection.";

            return null;
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] int? season,
            [FromQuery] int? week,
            [FromQuery] string? position,
            CancellationToken ct)
        {
            if (ValidateSeasonWeek(season, week) is { } error)
                return BadRequest(error);

            var results = string.IsNullOrWhiteSpace(position)
                ? await repo.GetByWeekAsync(season!.Value, week!.Value, ct)
                : await repo.GetByPositionAsync(season!.Value, week!.Value, position.ToUpper(), ct);

            return Ok(results);
        }

        [HttpPost("calculate")]
        public async Task<IActionResult> Calculate(
            [FromQuery] int? season,
            [FromQuery] int? week,
            CancellationToken ct)
        {
            if (ValidateSeasonWeek(season, week) is { } error)
                return BadRequest(error);

            var result = await mediator.Send(
                new CalculateProjectionsCommand(season!.Value, week!.Value), ct);

            return result.IsSuccess
                ? Ok(result.Value)
                : BadRequest(result.Error);
        }

        [HttpPost("simulate")]
        public async Task<IActionResult> Simulate(
            [FromQuery] int? season,
            [FromQuery] int? week,
            CancellationToken ct)
        {
            if (ValidateSeasonWeek(season, week) is { } error)
                return BadRequest(error);

            var result = await mediator.Send(
                new RunSimulationsCommand(season!.Value, week!.Value), ct);

            return result.IsSuccess
                ? Ok(result.Value)
                : BadRequest(result.Error);
        }

        [HttpGet("simulations")]
        public async Task<IActionResult> GetSimulations(
            [FromQuery] int? season,
            [FromQuery] int? week,
            [FromQuery] string? position,
            CancellationToken ct)
        {
            if (ValidateSeasonWeek(season, week) is { } error)
                return BadRequest(error);

            var results = string.IsNullOrWhiteSpace(position)
                ? await simulationRepo.GetByWeekAsync(season!.Value, week!.Value, ct)
                : await simulationRepo.GetByPositionAsync(season!.Value, week!.Value, position.ToUpper(), ct);

            return Ok(results);
        }

        [HttpPost("optimize")]
        public async Task<IActionResult> Optimize(
           [FromQuery] int? season,
           [FromQuery] int? week,
           [FromQuery] OptimizationMode mode = OptimizationMode.Median,
           [FromQuery] RiskProfile? riskProfile = null,
           [FromBody] OptimizeLineupRequest? request = null,
           CancellationToken ct = default)
        {
            if (ValidateSeasonWeek(season, week) is { } error)
                return BadRequest(error);

            var result = await mediator.Send(new OptimizeLineupCommand(
                season!.Value,
                week!.Value,
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
