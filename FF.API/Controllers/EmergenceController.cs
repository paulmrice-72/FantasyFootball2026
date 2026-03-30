// FF.API/Controllers/EmergenceController.cs
using FF.Application.Common;
using FF.Application.Features.EmergenceAlert.Commands;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/emergence")]
[Authorize]
public class EmergenceController(
    IEmergenceAlertRepository alertRepository,
    ICacheService cache,
    IMediator mediator) : ControllerBase
{
    [HttpPost("detect")]
    public async Task<IActionResult> Detect(
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct)
    {
        var result = await mediator.Send(new DetectEmergenceCommand(season, week), ct);

        // Bust both emergence cache keys so next GET reflects fresh detections
        cache.Remove(CacheKeys.EmergenceAlerts(season, null));
        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
            cache.Remove(CacheKeys.EmergenceAlerts(season, pos));

        return Ok(result);
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] int season,
        [FromQuery] string? position,
        CancellationToken ct)
    {
        var cacheKey = CacheKeys.EmergenceAlerts(season, position);

        var cached = cache.Get<IReadOnlyList<EmergenceAlertDocument>>(cacheKey);
        if (cached is not null)
            return Ok(cached);

        var alerts = await alertRepository.GetLatestBySeasonAsync(season, position, ct);

        if (alerts.Count > 0)
            cache.Set(cacheKey, alerts, TimeSpan.FromHours(1));

        return Ok(alerts);
    }

    [HttpGet("alerts/{season}/{week}")]
    public async Task<IActionResult> GetAlertsByWeek(
        int season,
        int week,
        [FromQuery] string? position,
        CancellationToken ct)
    {
        // Week-specific alerts are immutable once computed — cache for 4 hours
        var cacheKey = $"emergence:{season}:{week}:{position ?? "ALL"}";

        var cached = cache.Get<IReadOnlyList<EmergenceAlertDocument>>(cacheKey);
        if (cached is not null)
            return Ok(cached);

        var alerts = await alertRepository.GetBySeasonWeekAsync(season, week, position, ct);

        if (alerts.Count > 0)
            cache.Set(cacheKey, alerts, TimeSpan.FromHours(4));

        return Ok(alerts);
    }
}