using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class LeagueRepository : BaseRepository<League>, ILeagueRepository
{
    public LeagueRepository(FFDbContext context) : base(context) { }

    public async Task<League?> GetBySleeperIdAsync(string sleeperLeagueId, int season, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(l => l.SleeperLeagueId == sleeperLeagueId && l.Season == season, cancellationToken);

    public async Task<IReadOnlyList<League>> GetActiveLeaguesAsync(CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
}