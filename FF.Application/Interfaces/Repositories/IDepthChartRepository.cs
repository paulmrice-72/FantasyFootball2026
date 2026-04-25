// FF.Application/Interfaces/Repositories/IDepthChartRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

public interface IDepthChartRepository
{
    Task UpsertBatchAsync(IReadOnlyList<DepthChartDocument> rows, CancellationToken ct = default);
    Task<IReadOnlyList<DepthChartDocument>> GetByTeamAsync(string nflTeam, int season, int week, CancellationToken ct = default);
    Task<IReadOnlyList<DepthChartDocument>> GetByPlayerAsync(string sleeperPlayerId, int season, CancellationToken ct = default);
}