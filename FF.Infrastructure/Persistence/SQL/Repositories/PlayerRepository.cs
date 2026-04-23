using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using FF.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class PlayerRepository(FFDbContext context) : BaseRepository<Player>(context), IPlayerRepository
{
    public async Task<Player?> GetBySleeperIdAsync(string sleeperPlayerId, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(p => p.SleeperPlayerId == sleeperPlayerId, cancellationToken);

    public async Task<IReadOnlyList<Player>> GetByPositionAsync(
        Position position,
        CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Where(p => p.Position == position)
            .Where(p => p.FirstName != "Player" || p.LastName != "Invalid")
            .OrderBy(p => p.LastName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Player>> GetByNflTeamAsync(string nflTeam, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking()
            .Where(p => p.NflTeam == nflTeam)
            .OrderBy(p => p.Position)
            .ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Player>> GetRookiesAsync(
        string? position,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsNoTracking()
            .Where(p => p.YearsExperience == 0)
            .Where(p => p.FirstName != "Player" && p.LastName != "Invalid");  // ← add this

        if (!string.IsNullOrWhiteSpace(position))
        {
            if (Enum.TryParse<Position>(position, ignoreCase: true, out var posEnum))
                query = query.Where(p => p.Position == posEnum);
        }

        return await query
            .OrderBy(p => p.LastName)
            .ToListAsync(cancellationToken);
    }
    public async Task UpdateAsync(Player player, CancellationToken cancellationToken = default)
    {
        DbSet.Update(player);
        await Context.SaveChangesAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<Player>> GetBySleeperIdsAsync(
        IEnumerable<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default)
    => await DbSet.AsNoTracking()
        .Where(p => p.SleeperPlayerId != null && sleeperPlayerIds.Contains(p.SleeperPlayerId))
        .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Player>> GetPlayersNeedingCollegeBackfillAsync(
        CancellationToken cancellationToken = default)
    {
        return await Context.Players
            .Where(p => p.GsisId != null && p.CollegeTeam == null)
            .ToListAsync(cancellationToken);
    }

    public async new Task<IReadOnlyList<Player>> GetAllAsync(
    CancellationToken cancellationToken = default) =>
    await DbSet.AsNoTracking().ToListAsync(cancellationToken);
}