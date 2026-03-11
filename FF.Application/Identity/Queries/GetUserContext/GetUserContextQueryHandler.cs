using FF.Application.Identity.Interfaces;
using FF.Domain.ValueObjects;
using MediatR;

namespace FF.Application.Identity.Queries.GetUserContext;

public class GetUserContextQueryHandler(
    IUserRepository userRepository,
    ILeagueMembershipRepository leagueMembershipRepository
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