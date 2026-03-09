// FF.API/Controllers/UsageMetricsController.cs
using FF.Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/players")]
[Authorize]
public class UsageMetricsController(IUsageMetricsRepository repository) : ControllerBase
{
    private readonly IUsageMetricsRepository _repository = repository;

    // GET /api/v1/players/{playerId}/usage?season=2024
    [HttpGet("{playerId}/usage")]
    public async Task<IActionResult> GetUsageMetrics(
        string playerId,
        [FromQuery] int season = 2024,
        CancellationToken ct = default)
    {
        var metrics = await _repository
            .GetByPlayerSeasonAsync(playerId, season, ct);

        if (metrics is null)
            return NotFound(new { message = $"No usage metrics found for player {playerId} season {season}" });

        return Ok(metrics);
    }

    // GET /api/v1/players/usage?season=2024&position=WR
    [HttpGet("usage")]
    public async Task<IActionResult> GetUsageMetricsBySeason(
        [FromQuery] int season = 2024,
        [FromQuery] string? position = null,
        CancellationToken ct = default)
    {
        var metrics = await _repository
            .GetBySeasonAsync(season, position, ct);

        return Ok(metrics);
    }
}