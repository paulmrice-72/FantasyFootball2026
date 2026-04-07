using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Trade.Queries;

public class GetRedraftTradeValueQueryHandler(ISimulationResultRepository simulationRepository)
    : IRequestHandler<GetRedraftTradeValueQuery, List<RedraftTradeValueDto>>
{
    public async Task<List<RedraftTradeValueDto>> Handle(
        GetRedraftTradeValueQuery request,
        CancellationToken cancellationToken)
    {
        if (request.SleeperPlayerIds is null || request.SleeperPlayerIds.Count == 0)
            return [];

        var docs = await simulationRepository.GetLatestBySleeperIdsAsync(
            request.SleeperPlayerIds, 2026, cancellationToken);

        return docs.Select(d => new RedraftTradeValueDto(
            d.SleeperPlayerId ?? string.Empty,
            d.PlayerName,
            d.Position,
            d.NflTeam,
            (double)d.Median,
            ToValueLabel((double)d.Median)
        )).ToList();
    }

    private static string ToValueLabel(double median) => median switch
    {
        >= 20 => "Elite",
        >= 15 => "High",
        >= 10 => "Mid",
        >= 6 => "Low",
        _ => "Bench"
    };
}