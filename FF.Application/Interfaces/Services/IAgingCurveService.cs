using FF.Domain.Documents;

namespace FF.Application.Interfaces.Services;

public interface IAgingCurveService
{
    Task<List<AgingCurveDocument>> BuildAllCurvesAsync(CancellationToken ct = default);
    Task<double> GetAgeMultiplierAsync(string position, int age, CancellationToken ct = default);
    double EvaluateAtAge(AgingCurveDocument curve, int age);
}