using FF.Domain.Entities;
using FF.Domain.Enums;

namespace FF.Application.Interfaces.Persistence;

public interface IPlayerRepository : IRepository<Player>
{
    Task<Player?> GetBySleeperIdAsync(string sleeperPlayerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetByPositionAsync(Position position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetByNflTeamAsync(string nflTeam, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetRookiesAsync(string? position, CancellationToken cancellationToken = default);
    // FF.Domain/Repositories/IPlayerRepository.cs — add this method
    Task UpdateAsync(Player player, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetBySleeperIdsAsync(IEnumerable<string> sleeperPlayerIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetPlayersNeedingCollegeBackfillAsync(CancellationToken cancellationToken = default);
    new Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken cancellationToken = default);
}