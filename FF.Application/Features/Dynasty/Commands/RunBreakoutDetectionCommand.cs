using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using MediatR;

namespace FF.Application.Features.Dynasty.Commands;

public record RunBreakoutDetectionCommand(int Season) : IRequest<RunBreakoutDetectionResult>;
public record RunBreakoutDetectionResult(int Scored, TimeSpan Elapsed);

public class RunBreakoutDetectionCommandHandler(
    IBreakoutDetectionService detectionService,
    IDynastyValuationRepository valuationRepository)
    : IRequestHandler<RunBreakoutDetectionCommand, RunBreakoutDetectionResult>
{
    public async Task<RunBreakoutDetectionResult> Handle(
        RunBreakoutDetectionCommand request, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var valuations = await detectionService.ScoreAllPlayersAsync(request.Season, ct);
        await valuationRepository.UpsertBatchAsync(valuations, ct);
        sw.Stop();
        return new RunBreakoutDetectionResult(valuations.Count, sw.Elapsed);
    }
}