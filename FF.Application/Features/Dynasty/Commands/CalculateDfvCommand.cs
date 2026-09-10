using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Enums;
using MediatR;

namespace FF.Application.Features.Dynasty.Commands;

/// <param name="ScoringFormat">
/// FAN-158. Required, with no default. This carried <c>= ScoringFormat.HalfPpr</c>,
/// which the weekly recalculation job inherited without stating it while the admin
/// <c>run-dfv</c> endpoint defaulted to <c>Superflex</c> — two entry points writing
/// two different boards into one collection, last writer winning. A superflex roster
/// makes one flex slot QB-eligible and moves QB replacement level from 227.3 to 176.2
/// (FAN-153 phase 1), so this is a different quarterback ladder, not a label.
/// </param>
public record CalculateDfvCommand(
    int Season,
    ScoringFormat ScoringFormat,
    bool DisableVor = false)
    : IRequest<CalculateDfvResult>;

public record CalculateDfvResult(int Calculated, double MaxRawDfv, TimeSpan Elapsed);

public class CalculateDfvCommandHandler(
    IDfvCalculationService dfvService,
    IDynastyValuationRepository valuationRepository)
    : IRequestHandler<CalculateDfvCommand, CalculateDfvResult>
{
    public async Task<CalculateDfvResult> Handle(
        CalculateDfvCommand request,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var valuations = await dfvService.CalculateAllAsync(
            request.Season,
            request.ScoringFormat,
            request.DisableVor,
            ct);

        await valuationRepository.UpsertBatchAsync(valuations, ct);

        sw.Stop();

        var maxDfv = valuations.Count > 0
            ? valuations.Max(v => v.DiscountedFutureValue)
            : 0;

        return new CalculateDfvResult(valuations.Count, maxDfv, sw.Elapsed);
    }
}