using FF.Application.Identity.Interfaces;
using FF.Application.Interfaces.Persistence;
using FF.Domain.ValueObjects;
using MediatR;

namespace FF.Application.Identity.Queries.GetUserContext;

public class GetUserContextQueryHandler(
    IUserRepository userRepository,
    ILeagueMembershipRepository leagueMembershipRepository,
    ILeagueRepository leagueRepository,
    IUserLeaguePreferenceRepository leaguePreferenceRepository
) : IRequestHandler<GetUserContextQuery, UserContext?>
{
    public async Task<UserContext?> Handle(
        GetUserContextQuery request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return null;

        var leagues = await leagueMembershipRepository.GetLeaguesForUserAsync(
            request.UserId, cancellationToken);

        // Build SleeperLeagueId → Guid lookup to match preferences
        var preferences = await leaguePreferenceRepository.GetByUserIdAsync(
            request.UserId, cancellationToken);

        if (preferences.Count > 0)
        {
            var hiddenGuids = preferences
                .Where(p => p.IsHidden)
                .Select(p => p.LeagueId)
                .ToHashSet();

            // Load league entities to get SleeperLeagueId → Guid mapping
            var allLeagues = await leagueRepository.GetAllLeaguesAsync(cancellationToken);
            var hiddenSleeperIds = allLeagues
                .Where(l => hiddenGuids.Contains(l.Id))
                .Select(l => l.SleeperLeagueId)
                .ToHashSet();

            leagues = leagues
                .Where(l => !hiddenSleeperIds.Contains(l.LeagueId))
                .ToList();
        }

        return new UserContext(
            UserId: user.Id,
            SleeperUserId: user.SleeperUserId,
            SleeperUsername: user.SleeperUsername,
            IsSleeperLinked: user.SleeperUserId is not null,
            Leagues: leagues,
            ActiveLeagueId: leagues.FirstOrDefault(l => l.IsActive)?.LeagueId
        );
    }
}