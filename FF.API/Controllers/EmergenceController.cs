// FF.API/Controllers/EmergenceController.cs
using FF.Application.Features.EmergenceAlert.Commands;
using FF.Application.Interfaces.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/emergence")]
[Authorize]
public class EmergenceController(
    IEmergenceAlertRepository alertRepository,
    IMediator mediator) : ControllerBase
{
    [HttpPost("detect")]
    public async Task<IActionResult> Detect(
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct)
    {
        var result = await mediator.Send(new DetectEmergenceCommand(season, week), ct);
        return Ok(result);
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] int season,
        [FromQuery] string? position,
        CancellationToken ct)
    {
        var alerts = await alertRepository.GetLatestBySeasonAsync(season, position, ct);
        return Ok(alerts);
    }

    [HttpGet("alerts/{season}/{week}")]
    public async Task<IActionResult> GetAlertsByWeek(
        int season,
        int week,
        [FromQuery] string? position,
        CancellationToken ct)
    {
        var alerts = await alertRepository.GetBySeasonWeekAsync(season, week, position, ct);
        return Ok(alerts);
    }
}