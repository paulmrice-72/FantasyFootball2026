using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using FF.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class PlayerRepository : BaseRepository<Player>, IPlayerRepository
{
    public PlayerRepository(FFDbContext context) : base(context) { }

    public async Task<Player?> GetBySleeperIdAsync(string sleeperPlayerId, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(p => p.SleeperPlayerId == sleeperPlayerId, cancellationToken);

    public async Task<IReadOnlyList<Player>> GetByPositionAsync(Position position, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Where(p => p.Position == position)
            .OrderBy(p => p.LastName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Player>> GetByNflTeamAsync(string nflTeam, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Where(p => p.NflTeam == nflTeam)
            .OrderBy(p => p.Position)
            .ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);
}