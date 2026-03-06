using FF.Domain.Entities;

namespace FF.Application.Interfaces.Persistence;

public interface ILeagueRepository : IRepository<League>
{
    Task<League?> GetBySleeperIdAsync(string sleeperLeagueId, int season, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<League>> GetActiveLeaguesAsync(CancellationToken cancellationToken = default);
}