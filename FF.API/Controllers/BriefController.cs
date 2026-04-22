// FF.API/Controllers/BriefController.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using FF.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/brief")]
[Authorize]
public class BriefController(
    IWarRoomBriefRepository briefRepository,
    WarRoomBriefService briefService) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(
        [FromQuery] int season,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var brief = await briefRepository.GetLatestAsync(userId, season, ct);
        if (brief is null)
            return NotFound("No brief found for this season/week.");

        return Ok(brief);
    }

    [HttpPost("generate-all")]
    [Authorize(Roles = "Admin")]
    public IActionResult GenerateAll(
        [FromQuery] int? season,
        [FromQuery] int? week)
    {
        BackgroundJob.Enqueue<WarRoomBriefJob>(
            job => job.RunAsync(CancellationToken.None, true));

        return Accepted(new
        {
            Message = "Brief generation job queued — check Hangfire dashboard for progress.",
            Season = season,
            Week = week
        });
    }
}