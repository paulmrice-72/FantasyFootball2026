// FF.Application/Players/Commands/SyncPlayers/SyncPlayersCommand.cs
//
// Triggers a full sync of the Sleeper player universe into the local DB.
// Dispatched by both the API endpoint and the Hangfire weekly job.

using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Players.Commands.SyncPlayers;

/// <summary>
/// Pulls all NFL players from Sleeper and upserts them into the local Players table.
/// New players are inserted. Existing players get all fields overwritten.
/// Non-fantasy positions (LB, CB, OL, etc.) are skipped entirely.
/// </summary>
public record SyncPlayersCommand : IRequest<Result<SyncPlayersResult>>;

public record SyncPlayersResult(
    int PlayersAdded,
    int PlayersUpdated,
    int PlayersSkipped,
    int TotalProcessed,
    TimeSpan Duration
);
