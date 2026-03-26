using FF.Application.Interfaces.Services;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Dynasty.Commands;

public class BuildAgingCurvesCommandHandler(
    IAgingCurveService agingCurveService,
    IAgingCurveRepository agingCurveRepository)
    : IRequestHandler<BuildAgingCurvesCommand, List<AgingCurveDocument>>
{
    public async Task<List<AgingCurveDocument>> Handle(
        BuildAgingCurvesCommand request, CancellationToken ct)
    {
        var curves = await agingCurveService.BuildAllCurvesAsync(ct);

        foreach (var curve in curves)
            await agingCurveRepository.UpsertAsync(curve, ct);

        return curves;
    }
}