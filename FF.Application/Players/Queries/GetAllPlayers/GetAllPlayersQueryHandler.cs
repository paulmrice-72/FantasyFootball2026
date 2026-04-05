using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Players.Queries.GetAllPlayers;

public class GetAllPlayersQueryHandler
    : IRequestHandler<GetAllPlayersQuery, GetAllPlayersResponse>
{
    private readonly IPlayerRepository _playerRepository;

    public GetAllPlayersQueryHandler(IPlayerRepository playerRepository)
    {
        _playerRepository = playerRepository;
    }

    public async Task<GetAllPlayersResponse> Handle(
        GetAllPlayersQuery request,
        CancellationToken cancellationToken)
    {
        var players = await _playerRepository.GetAllAsync(cancellationToken);

        var dtos = players.Select(p => new PlayerDto(
            p.Id,
            p.FullName,
            p.Position.ToString(),
            p.NflTeam,
            p.Status.ToString()
        )).ToList();

        return new GetAllPlayersResponse(dtos, dtos.Count);
    }
}