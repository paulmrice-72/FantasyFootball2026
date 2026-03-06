using MediatR;

namespace FF.Application.Players.Queries.GetAllPlayers;

public record GetAllPlayersQuery : IRequest<GetAllPlayersResponse>;