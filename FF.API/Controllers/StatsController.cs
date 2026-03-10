// FF.API/Controllers/StatsController.cs

using FF.Application.Interfaces.Services;
using FF.Application.Stats.Commands.HistoricalImportStats;
using FF.Application.Stats.Queries.GetDataQuality;
using FF.Application.Stats.Queries.GetHistoricalStatsStatus;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class StatsController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator;

    /// <summary>
    /// Triggers a full historical stats import for all supported seasons (2022–2024).
    /// Includes PFR validation cross-check.
    /// Initial load — expect 30–90 seconds for full run.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ImportHistoricalStatsCommand(Seasons: null, RunPfrValidation: true),
            cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>
    /// Imports a single season. Useful if one season file was updated.
    /// Example: POST /api/v1/stats/import/2024
    /// Use ?validate=false to skip PFR cross-check for speed.
    /// </summary>
    [HttpPost("import/{season:int}")]
    public async Task<IActionResult> ImportSeason(
        int season,
        [FromQuery] bool validate = true,
        CancellationToken cancellationToken = default)
    {
        if (season < 2022 || season > 2024)
            return BadRequest(new { error = "Season must be 2022, 2023, or 2024" });

        var result = await _mediator.Send(
            new ImportHistoricalStatsCommand(
                Seasons: [season],
                RunPfrValidation: validate),
            cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns document counts per season from MongoDB PlayerGameLogs collection.
    /// Use after import to verify all seasons loaded correctly.
    /// Expected: ~1100 docs for 2022, ~1150 for 2023, ~1250 for 2024.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetHistoricalStatsStatusQuery(),
            cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error.Message });

        return Ok(result.Value);
    }

    /// <summary>
    /// Runs data quality validation across all imported PlayerGameLog documents.
    /// Checks document counts, stat ranges, missing fields, and position coverage.
    /// GET /api/v1/stats/quality
    /// </summary>
    [HttpGet("quality")]
    public async Task<IActionResult> GetDataQuality(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetDataQualityQuery(), cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error.Message });

        return Ok(result.Value);
    }

    [HttpPost("resolve-player-ids")]
    [ProducesResponseType(typeof(PlayerIdResolutionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolvePlayerIds(
    [FromServices] IPlayerIdResolutionService resolutionService,
    CancellationToken cancellationToken)
    {
        var result = await resolutionService.BackfillMissingSleeperIdsAsync(cancellationToken);
        return Ok(result);
    }
}