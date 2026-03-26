using FF.Domain.Documents;

namespace FF.Application.Interfaces.Services;

public interface IBreakoutDetectionService
{
    Task<List<DynastyValuationDocument>> ScoreAllPlayersAsync(int season, CancellationToken ct = default);
    BreakoutScoreResult ScorePlayer(
        FF.Domain.Entities.Player player,
        FF.Domain.Documents.PlayerUsageMetricsDocument? metrics,
        FF.Domain.Documents.CareerSimulationDocument? careerSim);
}

public record BreakoutScoreResult(
    double Score,
    FF.Domain.Enums.BreakoutClassification Classification,
    List<string> Signals);