using FF.Domain.Entities;
using FF.Domain.Enums;

namespace FF.Application.Interfaces.Persistence;

public interface IPlayerRepository : IRepository<Player>
{
    Task<Player?> GetBySleeperIdAsync(string sleeperPlayerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetByPositionAsync(Position position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetByNflTeamAsync(string nflTeam, CancellationToken cancellationToken = default);
}