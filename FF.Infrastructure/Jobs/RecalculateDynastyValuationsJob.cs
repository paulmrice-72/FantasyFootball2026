// FF.Infrastructure/Jobs/RecalculateDynastyValuationsJob.cs
using FF.Application.Features.Dynasty.Commands;
using FF.Domain.Enums;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Chains career simulation → breakout detection → DFV calculation.
/// Replaces the removed "Recalculate Values" button (PR #591).
/// Schedule: Wednesday 7:00 UTC — after SimulationJob (6am) and VegasLineSyncJob (5am).
///
/// <para>
/// <b>FAN-158.</b> The scoring format is now a required argument with no default.
/// This job used to call <c>new CalculateDfvCommand(season)</c> and silently inherit
/// that record's <c>HalfPpr</c> default, while the admin <c>run-dfv</c> endpoint
/// defaulted to <c>Superflex</c> — so the weekly job and the manual endpoint wrote two
/// different boards into the same collection and whichever ran last won. A manual
/// Superflex run would be quietly replaced by a HalfPpr one the next Wednesday.
/// </para>
///
/// <para>
/// The difference is not cosmetic. A superflex roster makes one flex slot QB-eligible,
/// which moved measured QB replacement level from 227.3 to 176.2 (FAN-153 phase 1) —
/// a materially different quarterback ladder, which is most of the dynasty board.
/// </para>
/// </summary>
public class RecalculateDynastyValuationsJob(
    IMediator mediator,
    ILogger<RecalculateDynastyValuationsJob> logger)
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<RecalculateDynastyValuationsJob> _logger = logger;

    /// <param name="scoringFormat">
    /// The format the served board is priced for. Required — see the class remarks.
    /// </param>
    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(int season, ScoringFormat scoringFormat, CancellationToken ct)
    {
        _logger.LogInformation(
            "RecalculateDynastyValuationsJob starting — Season {Season}, format {Format}",
            season, scoringFormat);

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
        var dfvResult = await _mediator.Send(new CalculateDfvCommand(season, scoringFormat), ct);
        _logger.LogInformation(
            "DFV calculation complete — Format: {Format}, Calculated: {Calculated}, MaxRawDfv: {MaxRawDfv:F2}, Elapsed: {Elapsed:F1}s",
            scoringFormat, dfvResult.Calculated, dfvResult.MaxRawDfv, dfvResult.Elapsed.TotalSeconds);

        _logger.LogInformation(
            "RecalculateDynastyValuationsJob finished — Season {Season} ({Format}) pipeline complete.",
            season, scoringFormat);
    }
}