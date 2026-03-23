using FF.Domain.Entities;
using FF.Domain.ValueObjects;

namespace FF.Application.Identity.Interfaces;

public interface ILeagueMembershipRepository
{
    Task<IReadOnlyList<LeagueContext>> GetLeaguesForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task AddMembershipAsync(string userId, string sleeperUserId, string leagueId, string leagueName, int season, string role, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeagueMembership>> GetMembershipsForUserAsync(string userId, CancellationToken cancellationToken = default);
}