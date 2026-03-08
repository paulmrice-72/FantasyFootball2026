// FF.Application/Interfaces/Services/ISleeperPlayerSyncService.cs

using FF.Application.Players.Commands.SyncPlayers;

namespace FF.Application.Interfaces.Services;

public interface ISleeperPlayerSyncService
{
    /// <summary>
    /// Fetches all NFL players from Sleeper and upserts them into the local DB.
    /// New players are inserted. Existing players have all fields overwritten.
    /// Non-fantasy positions are skipped.
    /// </summary>
    Task<SyncPlayersResult> SyncAllPlayersAsync(CancellationToken cancellationToken = default);
}
