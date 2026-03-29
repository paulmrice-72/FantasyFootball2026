// FF.Infrastructure/Jobs/EmergenceDetectionJob.cs
using FF.Application.Features.EmergenceAlert.Commands;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class EmergenceDetectionJob(
    IMediator mediator,
    ILogger<EmergenceDetectionJob> logger)
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<EmergenceDetectionJob> _logger = logger;

    /// <summary>
    /// Scans all active players for in-season role emergence signals.
    /// Runs every Tuesday at noon UTC — after usage-metrics-aggregation completes.
    /// Register in DependencyInjection.cs:
    ///   RecurringJob.AddOrUpdate{EmergenceDetectionJob}(
    ///       "emergence-detection-weekly",
    ///       job => job.RunAsync(0, 0),
    ///       "0 12 * * 2");
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(int season, int week)
    {
        _logger.LogInformation(
            "EmergenceDetectionJob starting — Season {Season} Week {Week}", season, week);

        var result = await _mediator.Send(new DetectEmergenceCommand(season, week));

        _logger.LogInformation(
            "EmergenceDetectionJob complete — {Scanned} scanned, {Alerts} alerts generated",
            result.PlayersScanned, result.AlertsGenerated);
    }
}