// FF.Application/Queries/GetWaiverRecommendationsQuery.cs
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.WaiverRecommendations.Queries.GetWaiverRecommendations;

public record GetWaiverRecommendationsQuery(
    string SleeperLeagueId,
    int Season,
    int Week,
    string? Position = null,
    int Top = 30) : IRequest<IReadOnlyList<VorpRecommendationDocument>>;