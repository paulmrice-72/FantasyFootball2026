// FF.Application/Leagues/Commands/ImportLeague/ImportLeagueCommand.cs
//
// MediatR command that drives the full Sleeper league import.
// The controller and Hangfire job both dispatch this command —
// that's the point of CQRS. One piece of business logic, two triggers.

using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Leagues.Commands.ImportLeague;

/// <summary>
/// Triggers a full import of a Sleeper league into the local database.
/// Imports: league settings, all rosters, all owners, and transaction
/// history for the last 2 seasons.
/// Safe to call multiple times — all writes are idempotent upserts.
/// </summary>
public record ImportLeagueCommand(string SleeperLeagueId) : IRequest<Result<ImportLeagueResult>>;

/// <summary>
/// Summary of what was imported/updated during the operation.
/// Returned to the caller so the API response has useful information.
/// </summary>
public record ImportLeagueResult(
    string LeagueName,
    string LeagueId,
    int RostersImported,
    int PlayersImported,
    int TransactionsImported,
    bool WasNewLeague
);
