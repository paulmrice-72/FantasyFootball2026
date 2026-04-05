using MediatR;

namespace FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;

public record GetOffSeasonAvailablePlayersQuery(
    string LeagueId,
    string? Position = null,
    int Top = 50) : IRequest<IReadOnlyList<OffSeasonAvailablePlayerDto>>;