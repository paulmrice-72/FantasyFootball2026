using FF.Application.Identity.Interfaces;
using FF.Domain.Entities;
using FF.Domain.ValueObjects;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class LeagueMembershipRepository(FFDbContext dbContext) : ILeagueMembershipRepository
{
    public async Task<IReadOnlyList<LeagueContext>> GetLeaguesForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.LeagueMemberships
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => new LeagueContext(
                m.LeagueId,
                m.LeagueName,
                m.Season,
                m.Role,
                m.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task AddMembershipAsync(
        string userId,
        string sleeperUserId,
        string leagueId,
        string leagueName,
        int season,
        string role,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.LeagueMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId
                && m.LeagueId == leagueId
                && m.Season == season, cancellationToken);

        if (existing is not null)
            return;

        dbContext.LeagueMemberships.Add(new LeagueMembership
        {
            UserId = userId,
            SleeperUserId = sleeperUserId,
            LeagueId = leagueId,
            LeagueName = leagueName,
            Season = season,
            Role = role,
            IsActive = true,
            LinkedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}