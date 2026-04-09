// FF.API/Controllers/TeamController.cs
using FF.Application.Features.Lineups.Commands.OptimizeLineup;
using FF.Application.Features.Team.Queries;
using FF.Application.Services.LineupOptimizer;
using FF.Domain.Enums;
using FF.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/team")]
[Authorize]
public class TeamController(IMediator mediator, UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet("roster")]
    public async Task<IActionResult> GetMyRoster(
        [FromQuery] string sleeperLeagueId,
        CancellationToken ct)
    {
        var appUser = await GetAppUserAsync();
        if (appUser?.SleeperUserId is null) return BadRequest("Sleeper account not linked.");
        if (string.IsNullOrEmpty(sleeperLeagueId)) return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetMyRosterQuery(appUser.SleeperUserId, sleeperLeagueId), ct);

        return result is null ? NotFound("Roster not found.") : Ok(result);
    }

    [HttpPost("optimize-lineup")]
    public async Task<IActionResult> OptimizeMyLineup(
        [FromQuery] string sleeperLeagueId,
        [FromQuery] int season,
        [FromQuery] int week,
        [FromQuery] string mode = "Median",
        [FromQuery] string? riskProfile = null,
        CancellationToken ct = default)
    {
        var appUser = await GetAppUserAsync();
        if (appUser?.SleeperUserId is null) return BadRequest("Sleeper account not linked.");
        if (string.IsNullOrEmpty(sleeperLeagueId)) return BadRequest("sleeperLeagueId is required.");

        var roster = await mediator.Send(
            new GetMyRosterQuery(appUser.SleeperUserId, sleeperLeagueId), ct);
        if (roster is null) return NotFound("Roster not found.");

        var rosterSleeperIds = roster.Players
            .Select(p => p.SleeperPlayerId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        if (!Enum.TryParse<OptimizationMode>(mode, true, out var optimizationMode))
            optimizationMode = OptimizationMode.Median;

        RiskProfile? parsedRisk = null;
        if (!string.IsNullOrEmpty(riskProfile) &&
            Enum.TryParse<RiskProfile>(riskProfile, true, out var rp))
            parsedRisk = rp;

        var result = await mediator.Send(new OptimizeLineupCommand(
            Season: season,
            Week: week,
            Mode: optimizationMode,
            RiskProfile: parsedRisk,
            RosterSleeperIds: rosterSleeperIds), ct);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error.Message);
    }

    // TEAM-003: current week matchup
    [HttpGet("matchup")]
    public async Task<IActionResult> GetMyMatchup(
        [FromQuery] string sleeperLeagueId,
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct = default)
    {
        var appUser = await GetAppUserAsync();
        if (appUser?.SleeperUserId is null) return BadRequest("Sleeper account not linked.");
        if (string.IsNullOrEmpty(sleeperLeagueId)) return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetMyMatchupQuery(appUser.SleeperUserId, sleeperLeagueId, season, week), ct);

        return result is null ? NotFound("Matchup not found for this week.") : Ok(result);
    }

    // ── Shared helper ────────────────────────────────────────────────────────
    private async Task<ApplicationUser?> GetAppUserAsync()
    {
        var internalUserId = User
            .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? string.Empty;
        return await userManager.FindByIdAsync(internalUserId);
    }
}