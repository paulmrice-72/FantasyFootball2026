// FF.Infrastructure/Jobs/CalibrationValidationJob.cs
using FF.Application.Features.Calibration.Commands;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class CalibrationValidationJob(
    IMediator mediator,
    ILogger<CalibrationValidationJob> logger)
{
    [AutomaticRetry(Attempts = 1)]
    public async Task RunAsync(int season, string scoringFormat = "Superflex", CancellationToken ct = default)
    {
        logger.LogInformation(
            "CalibrationValidationJob starting — Season {Season}, Format {Format}",
            season, scoringFormat);

        var result = await mediator.Send(
            new RunCalibrationCommand(season, scoringFormat), ct);

        logger.LogInformation(
            "Calibration complete — ρ={Rho:F4}, AvgDelta={Delta:F2}, Top10Overlap={Overlap}/10, Players={Count}",
            result.SpearmanRho, result.AvgAbsDelta, result.Top10Overlap, result.PlayerCount);
    }
}