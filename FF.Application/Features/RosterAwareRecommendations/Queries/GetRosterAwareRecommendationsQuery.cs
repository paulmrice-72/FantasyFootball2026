// FF.Application/Queries/GetRosterAwareRecommendationsQuery.cs
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.RosterAwareRecommendations.Queries;

public record GetRosterAwareRecommendationsQuery(
    string SleeperLeagueId,
    string SleeperUserId,
    int Season,
    int Week,
    string? Position = null,
    int Top = 30) : IRequest<IReadOnlyList<RosterAwareRecommendation>>;