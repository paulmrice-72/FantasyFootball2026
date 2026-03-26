using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using MediatR;

namespace FF.Application.Features.Dynasty.Commands;

public record CalculateDfvCommand(int Season) : IRequest<CalculateDfvResult>;
public record CalculateDfvResult(int Calculated, double MaxRawDfv, TimeSpan Elapsed);

public class CalculateDfvCommandHandler(
    IDfvCalculationService dfvService,
    IDynastyValuationRepository valuationRepository)
    : IRequestHandler<CalculateDfvCommand, CalculateDfvResult>
{
    public async Task<CalculateDfvResult> Handle(
        CalculateDfvCommand request, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var valuations = await dfvService.CalculateAllAsync(request.Season, ct);
        await valuationRepository.UpsertBatchAsync(valuations, ct);
        sw.Stop();

        var maxDfv = valuations.Count > 0
            ? valuations.Max(v => v.DiscountedFutureValue)
            : 0;

        return new CalculateDfvResult(valuations.Count, maxDfv, sw.Elapsed);
    }
}