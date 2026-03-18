// FF.Infrastructure/Persistence/Sql/Repositories/UserProjectionWeightProfileRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using FF.Infrastructure.Persistence;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.Sql.Repositories;

public class UserProjectionWeightProfileRepository(FFDbContext db)
    : IUserProjectionWeightProfileRepository
{
    public async Task<UserProjectionWeightProfile?> GetActiveByUserAsync(
        string appUserId, CancellationToken ct = default) =>
        await db.UserProjectionWeightProfiles
            .FirstOrDefaultAsync(x => x.AppUserId == appUserId && x.IsActive, ct);

    public async Task UpsertAsync(
        UserProjectionWeightProfile profile, CancellationToken ct = default)
    {
        var existing = await db.UserProjectionWeightProfiles
            .FirstOrDefaultAsync(x => x.AppUserId == profile.AppUserId && x.IsActive, ct);

        if (existing is null)
            db.UserProjectionWeightProfiles.Add(profile);

        await db.SaveChangesAsync(ct);
    }
}