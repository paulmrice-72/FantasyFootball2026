// FF.API/Controllers/InjuryController.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Application.Common;
using FF.Domain.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/injuries")]
[Authorize]
public class InjuryController(
    IInjuryAlertRepository injuryAlertRepository,
    ICacheService cache) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetInjuryAlerts(
        [FromQuery] string? position,
        CancellationToken ct)
    {
        var cacheKey = $"injuries:{position ?? "ALL"}";

        var cached = cache.Get<IReadOnlyList<InjuryAlertDocument>>(cacheKey);
        if (cached is not null)
            return Ok(cached);

        var alerts = await injuryAlertRepository.GetActiveAlertsAsync(position, ct);

        if (alerts.Count > 0)
            cache.Set(cacheKey, alerts, TimeSpan.FromMinutes(30));

        return Ok(alerts);
    }
}