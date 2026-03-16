// FF.API/Controllers/ProjectionsController.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers
{
    [ApiController]
    [Route("api/v1/projections")]
    //[Authorize]
    public class ProjectionsController(IPlayerProjectionRepository repo) : ControllerBase
    {
        private readonly IPlayerProjectionRepository _repo = repo;

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
                ? await _repo.GetByWeekAsync(season, week, ct)
                : await _repo.GetByPositionAsync(season, week, position.ToUpper(), ct);

            return Ok(results);
        }

        [HttpPost("calculate")]
        public async Task<IActionResult> Calculate()
        {
            // TODO PBI-026: move this to a Hangfire job
            // For now: manual trigger for dev/testing
            return Accepted(new { message = "Projection calculation is triggered via Hangfire in PBI-026. Use the Hangfire dashboard." });
        }
    }
}