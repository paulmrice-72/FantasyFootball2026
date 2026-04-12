using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using FF.Infrastructure.Persistence.SQL;
using FF.Infrastructure.Persistence.SQL.Repositories;
using Microsoft.EntityFrameworkCore;
using static OperationsResearch.SetCoverProto.Types;

namespace FF.Infrastructure.Persistence.Sql.Repositories;

public class UserLeaguePreferenceRepository(FFDbContext context)
    : BaseRepository<UserLeaguePreference>(context), IUserLeaguePreferenceRepository
{
    public async Task<IReadOnlyList<UserLeaguePreference>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
        => await DbSet.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

    public async Task<UserLeaguePreference?> GetAsync(
        string userId, Guid leagueId, CancellationToken ct = default)
        => await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LeagueId == leagueId, ct);

    public async Task UpsertAsync(UserLeaguePreference preference, CancellationToken ct = default)
    {
        var existing = await DbSet
            .FirstOrDefaultAsync(p => p.UserId == preference.UserId
                                   && p.LeagueId == preference.LeagueId, ct);
        if (existing is null)
            DbSet.Add(preference);
        else
            existing.SetVisibility(preference.IsHidden);

        await context.SaveChangesAsync(ct);
    }
}