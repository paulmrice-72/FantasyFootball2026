// FF.Application/Players/Commands/SyncPlayers/SyncPlayersCommandHandler.cs

using FF.Application.Interfaces.Services;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Players.Commands.SyncPlayers;

public class SyncPlayersCommandHandler(
    ISleeperPlayerSyncService syncService,
    ILogger<SyncPlayersCommandHandler> logger)
        : IRequestHandler<SyncPlayersCommand, Result<SyncPlayersResult>>
{
    private readonly ISleeperPlayerSyncService _syncService = syncService;
    private readonly ILogger<SyncPlayersCommandHandler> _logger = logger;

    public async Task<Result<SyncPlayersResult>> Handle(
        SyncPlayersCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Sleeper player universe sync");
        var started = DateTime.UtcNow;

        try
        {
            var result = await _syncService.SyncAllPlayersAsync(cancellationToken);

            _logger.LogInformation(
                "Player sync complete in {Duration:0.0}s — " +
                "Added: {Added}, Updated: {Updated}, Skipped: {Skipped}",
                result.Duration.TotalSeconds,
                result.PlayersAdded,
                result.PlayersUpdated,
                result.PlayersSkipped);

            return Result<SyncPlayersResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Player sync failed after {Elapsed:0.0}s",
                (DateTime.UtcNow - started).TotalSeconds);

            return Result.Failure<SyncPlayersResult>(
                new Error("PlayerSync.Failed", ex.Message));
        }
    }
}
