using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class RosterRepository : BaseRepository<Roster>, IRosterRepository
{
    public RosterRepository(FFDbContext context) : base(context) { }

    public async Task<IReadOnlyList<Roster>> GetByLeagueIdAsync(Guid leagueId, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Where(r => r.LeagueId == leagueId)
            .OrderBy(r => r.TeamName)
            .ToListAsync(cancellationToken);

    public async Task<Roster?> GetBySleeperRosterIdAsync(string sleeperRosterId, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(r => r.SleeperRosterId == sleeperRosterId, cancellationToken);
}