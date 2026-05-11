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

    // TEAM-004: Start/Sit recommendations
    [HttpGet("start-sit")]
    public async Task<IActionResult> GetStartSitRecommendations(
        [FromQuery] string sleeperLeagueId,
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct = default)
    {
        var appUser = await GetAppUserAsync();
        if (appUser?.SleeperUserId is null) return BadRequest("Sleeper account not linked.");
        if (string.IsNullOrEmpty(sleeperLeagueId)) return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetStartSitRecommendationsQuery(
                appUser.SleeperUserId, sleeperLeagueId, season, week), ct);

        return result is null
            ? NotFound("No roster found or insufficient data for recommendations.")
            : Ok(result);
    }

    // TEAM-005: Positional depth grades
    [HttpGet("depth-grades")]
    public async Task<IActionResult> GetPositionalDepthGrades(
        [FromQuery] string sleeperLeagueId,
        [FromQuery] int season,
        CancellationToken ct = default)
    {
        var appUser = await GetAppUserAsync();
        if (appUser?.SleeperUserId is null) return BadRequest("Sleeper account not linked.");
        if (string.IsNullOrEmpty(sleeperLeagueId)) return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetPositionalDepthGradesQuery(
                appUser.SleeperUserId, sleeperLeagueId, season), ct);

        return result is null
            ? NotFound("No roster found or insufficient data.")
            : Ok(result);
    }

    // TEAM-006: Dynasty team grade
    [HttpGet("dynasty-grade")]
    public async Task<IActionResult> GetDynastyTeamGrade(
        [FromQuery] string sleeperLeagueId,
        CancellationToken ct = default)
    {
        var appUser = await GetAppUserAsync();
        if (appUser?.SleeperUserId is null) return BadRequest("Sleeper account not linked.");
        if (string.IsNullOrEmpty(sleeperLeagueId)) return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetDynastyTeamGradeQuery(
                appUser.SleeperUserId, sleeperLeagueId), ct);

        return result is null
            ? NotFound("No roster or dynasty valuation data found.")
            : Ok(result);
    }

    // TEAM-007: Draft prep dashboard
    [HttpGet("draft-prep")]
    public async Task<IActionResult> GetDraftPrep(
        [FromQuery] string sleeperLeagueId,
        [FromQuery] int simSeason,
        [FromQuery] int rookieSeason,
        CancellationToken ct = default)
    {
        var appUser = await GetAppUserAsync();
        if (appUser?.SleeperUserId is null)
            return BadRequest("Sleeper account not linked.");
        if (string.IsNullOrEmpty(sleeperLeagueId))
            return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetDraftPrepQuery(appUser.SleeperUserId, sleeperLeagueId, simSeason, rookieSeason), ct);

        return result is null ? NotFound() : Ok(result);
    }

    // FAN-66 / LINEUP-CARD-001: Full optimized lineup card
    // Returns all starting slots (QB/RB1/RB2/WR1-3/TE/FLEX/SUPERFLEX) plus bench.
    // Driven by LineupOptimizerService — same engine as optimize-lineup but
    // returns a slot-labeled card rather than a raw optimizer result.
    [HttpGet("lineup-card")]
    public async Task<IActionResult> GetLineupCard(
        [FromQuery] string sleeperLeagueId,
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct = default)
    {
        var appUser = await GetAppUserAsync();
        if (appUser?.SleeperUserId is null) return BadRequest("Sleeper account not linked.");
        if (string.IsNullOrEmpty(sleeperLeagueId)) return BadRequest("sleeperLeagueId is required.");

        var result = await mediator.Send(
            new GetLineupCardQuery(
                appUser.SleeperUserId, sleeperLeagueId, season, week), ct);

        return result is null
            ? NotFound("No roster or simulation data found for lineup card.")
            : Ok(result);
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