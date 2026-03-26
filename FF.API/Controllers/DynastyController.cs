using FF.Application.Features.Dynasty.Commands;
using FF.Application.Features.Dynasty.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/dynasty")]
[Authorize]
public class DynastyController(IMediator mediator) : ControllerBase
{
    [HttpPost("aging-curves/build")]
    public async Task<IActionResult> BuildAgingCurves(CancellationToken ct)
    {
        var curves = await mediator.Send(new BuildAgingCurvesCommand(), ct);
        return Ok(new { built = curves.Count, positions = curves.Select(c => c.Position) });
    }

    [HttpGet("aging-curves/{position}")]
    public async Task<IActionResult> GetAgingCurve(string position, CancellationToken ct)
    {
        var curve = await mediator.Send(new GetAgingCurveQuery(position.ToUpper()), ct);
        return curve is null ? NotFound() : Ok(curve);
    }

    [HttpPost("career-simulations/run")]
    public async Task<IActionResult> RunCareerSimulations(
    [FromQuery] int season, CancellationToken ct)
    {
        if (season <= 0) season = 2026;
        var result = await mediator.Send(new RunCareerSimulationsCommand(season), ct);
        return Ok(new
        {
            result.Simulated,
            result.Failed,
            ElapsedSeconds = result.Elapsed.TotalSeconds
        });
    }

    [HttpGet("career-simulations/{sleeperPlayerId}")]
    public async Task<IActionResult> GetCareerSimulation(
        string sleeperPlayerId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetCareerSimulationQuery(sleeperPlayerId), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("breakout/run")]
    public async Task<IActionResult> RunBreakoutDetection(
    [FromQuery] int season, CancellationToken ct)
    {
        if (season <= 0) season = 2026;
        var result = await mediator.Send(new RunBreakoutDetectionCommand(season), ct);
        return Ok(new { result.Scored, ElapsedSeconds = result.Elapsed.TotalSeconds });
    }

    [HttpGet("valuations")]
    public async Task<IActionResult> GetValuations(
        [FromQuery] string? position,
        [FromQuery] int top = 50,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetDynastyValuationsQuery(position, top), ct);
        return Ok(result);
    }

    [HttpPost("dfv/calculate")]
    public async Task<IActionResult> CalculateDfv(
    [FromQuery] int season, CancellationToken ct)
    {
        if (season <= 0) season = 2026;
        var result = await mediator.Send(new CalculateDfvCommand(season), ct);
        return Ok(new
        {
            result.Calculated,
            result.MaxRawDfv,
            ElapsedSeconds = result.Elapsed.TotalSeconds
        });
    }
}