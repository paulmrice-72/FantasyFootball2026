// FF.Infrastructure/Jobs/RecalculateDynastyValuationsJob.cs
using FF.Application.Features.Dynasty.Commands;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Chains career simulation → breakout detection → DFV calculation.
/// Replaces the removed "Recalculate Values" button (PR #591).
/// Schedule: Wednesday 7:00 UTC — after SimulationJob (6am) and VegasLineSyncJob (5am).
/// </summary>
public class RecalculateDynastyValuationsJob(
    IMediator mediator,
    ILogger<RecalculateDynastyValuationsJob> logger)
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<RecalculateDynastyValuationsJob> _logger = logger;

    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(int season, CancellationToken ct)
    {
        _logger.LogInformation(
            "RecalculateDynastyValuationsJob starting — Season {Season}", season);

        // Step 1 — Career simulations
        _logger.LogInformation("Step 1/3: Running career simulations...");
        var simResult = await _mediator.Send(new RunCareerSimulationsCommand(season), ct);
        _logger.LogInformation(
            "Career simulations complete — Simulated: {Simulated}, Failed: {Failed}, Elapsed: {Elapsed:F1}s",
            simResult.Simulated, simResult.Failed, simResult.Elapsed.TotalSeconds);

        // Step 2 — Breakout detection
        _logger.LogInformation("Step 2/3: Running breakout detection...");
        var breakoutResult = await _mediator.Send(new RunBreakoutDetectionCommand(season), ct);
        _logger.LogInformation(
            "Breakout detection complete — Scored: {Scored}, Elapsed: {Elapsed:F1}s",
            breakoutResult.Scored, breakoutResult.Elapsed.TotalSeconds);

        // Step 3 — DFV calculation
        _logger.LogInformation("Step 3/3: Calculating DFV...");
        var dfvResult = await _mediator.Send(new CalculateDfvCommand(season), ct);
        _logger.LogInformation(
            "DFV calculation complete — Calculated: {Calculated}, MaxRawDfv: {MaxRawDfv:F2}, Elapsed: {Elapsed:F1}s",
            dfvResult.Calculated, dfvResult.MaxRawDfv, dfvResult.Elapsed.TotalSeconds);

        _logger.LogInformation(
            "RecalculateDynastyValuationsJob finished — Season {Season} pipeline complete.", season);
    }
}