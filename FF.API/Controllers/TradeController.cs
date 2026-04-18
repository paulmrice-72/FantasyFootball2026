// FF.API/Controllers/TradeController.cs
using FF.Application.Features.Dynasty.Commands;
using FF.Application.Features.Dynasty.Queries;
using FF.Application.Features.Trade.Queries;
using FF.Application.Features.Trade.Queries.GetLeagueTradeContext;
using FF.Application.Features.Trade.Queries.GetRedraftTradeValue;
using FF.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/trade")]
[Authorize]
public class TradeController(IMediator mediator, UserManager<ApplicationUser> userManager) : ControllerBase
{
    // ── Generic dynasty trade analyze ───────────────────────────────────────
    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeTrade(
        [FromBody] AnalyzeTradeRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? string.Empty;

        var result = await mediator.Send(new AnalyzeTradeCommand(
            userId,
            request.MyPlayerIds,
            request.TheirPlayerIds,
            request.MyPicks  ?? [],
            request.TheirPicks ?? [],
            request.Season > 0 ? request.Season : 2026,
            request.LeagueId,
            request.SleeperUserId), ct);

        return Ok(result);
    }

    // ── League trade context ────────────────────────────────────────────────
    [HttpGet("league-context")]
    public async Task<IActionResult> GetLeagueContext(
        [FromQuery] string leagueId,
        [FromQuery] int season = 2026,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leagueId))
            return BadRequest("leagueId is required.");

        var internalUserId = User
            .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? string.Empty;
        var appUser = await userManager.FindByIdAsync(internalUserId);
        if (appUser?.SleeperUserId is null)
            return BadRequest("Sleeper account not linked.");

        var sleeperUserId = appUser.SleeperUserId;

        var result = await mediator.Send(
            new GetLeagueTradeContextQuery(leagueId, sleeperUserId, season), ct);

        return Ok(result);
    }

    // ── Trade history ───────────────────────────────────────────────────────
    [HttpGet("history")]
    public async Task<IActionResult> GetTradeHistory(CancellationToken ct)
    {
        var userId = User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? string.Empty;

        var result = await mediator.Send(new GetTradeHistoryQuery(userId), ct);
        return Ok(result);
    }

    // ── Redraft trade value ─────────────────────────────────────────────────
    [HttpGet("value/redraft")]
    public async Task<IActionResult> GetRedraftValues(
        [FromQuery] string playerIds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(playerIds))
            return BadRequest("playerIds query parameter is required.");

        var ids = playerIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries
                      | StringSplitOptions.TrimEntries)
            .ToList();

        var result = await mediator.Send(new GetRedraftTradeValueQuery(ids), ct);
        return Ok(result);
    }

    // ── Request DTOs ────────────────────────────────────────────────────────
    public record AnalyzeTradeRequest(
        List<string> MyPlayerIds,
        List<string> TheirPlayerIds,
        List<TradePickRequest>? MyPicks,
        List<TradePickRequest>? TheirPicks,
        int Season,
        string? LeagueId      = null,  // null = generic mode
        string? SleeperUserId = null); // required when LeagueId present
}
