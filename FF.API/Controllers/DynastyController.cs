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
}