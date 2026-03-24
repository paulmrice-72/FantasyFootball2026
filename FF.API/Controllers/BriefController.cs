// FF.API/Controllers/BriefController.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/brief")]
[Authorize]
public class BriefController(
    IWarRoomBriefRepository briefRepository,
    WarRoomBriefService briefService,
    ICoachRileyService coachRileyService) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(
        [FromQuery] int season,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var brief = await briefRepository.GetLatestAsync(userId, season, ct);
        if (brief is null) return NotFound("No brief found. Trigger generation first.");

        return Ok(brief);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var brief = await briefService.GenerateBriefAsync(
            userId, email, season, week, ct);

        return Ok(brief);
    }

    // DEV ONLY — delete before commit
    [HttpPost("test-riley")]
    public async Task<IActionResult> TestRiley(CancellationToken ct)
    {
        var brief = new WarRoomBriefDocument
        {
            Season = 2025,
            Week = 10,
            TopBoomCandidates =
            [
                new() { PlayerName = "Justin Jefferson", Position = "WR",
                    NflTeam = "MIN", OpponentTeam = "LAR",
                    BoomProbability = 0.42m, HighlightReason = "Soft CB matchup, target hog" }
            ],
            BustRisks =
            [
                new() { PlayerName = "Davante Adams", Position = "WR",
                    NflTeam = "NYJ", OpponentTeam = "KC",
                    BustProbability = 0.38m, HighlightReason = "Shadow coverage from Sauce Gardner" }
            ],
            Leagues =
            [
                new() { LeagueName = "Bizarro League", TeamName = "Paul's Squad" }
            ]
        };

        var narrative = await coachRileyService.GenerateNarrativeAsync(brief, ct);
        return Ok(new { narrative });
    }
}