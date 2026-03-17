// FF.API/Controllers/ProjectionsController.cs
using FF.Application.Features.Projections.Commands.CalculateProjections;
using FF.Application.Interfaces.Persistence;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers
{
    [ApiController]
    [Route("api/v1/projections")]
    // [Authorize]
    public class ProjectionsController(
        IPlayerProjectionRepository repo,
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
    }
}