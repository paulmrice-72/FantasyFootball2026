// FF.Application/Leagues/Commands/ImportLeague/ImportLeagueCommandHandler.cs
//
// Handles the ImportLeagueCommand. Coordinates between:
//   - ISleeperLeagueImportService (infrastructure, does the actual work)
//   - IUnitOfWork (commits the transaction)
//
// The handler itself stays thin — it validates, calls the service, and returns.
// All Sleeper API calls and EF Core writes happen in the infrastructure service.

using FF.Application.Interfaces.Services;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Leagues.Commands.ImportLeague;

public class ImportLeagueCommandHandler(
    ISleeperLeagueImportService importService,
    ILogger<ImportLeagueCommandHandler> logger)
        : IRequestHandler<ImportLeagueCommand, Result<ImportLeagueResult>>
{
    private readonly ISleeperLeagueImportService _importService = importService;
    private readonly ILogger<ImportLeagueCommandHandler> _logger = logger;

    public async Task<Result<ImportLeagueResult>> Handle(
        ImportLeagueCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting league import for Sleeper league {LeagueId}",
            request.SleeperLeagueId);

        try
        {
            var result = await _importService.ImportLeagueAsync(
                request.SleeperLeagueId,
                cancellationToken);

            _logger.LogInformation(
                "League import complete for {LeagueName}. " +
                "Rosters: {Rosters}, Players: {Players}, Transactions: {Transactions}",
                result.LeagueName,
                result.RostersImported,
                result.PlayersImported,
                result.TransactionsImported);

            return Result<ImportLeagueResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "League import failed for Sleeper league {LeagueId}",
                request.SleeperLeagueId);

            // FIXED - call Failure<T> directly on the static Result class
            return Result.Failure<ImportLeagueResult>(
                new Error("LeagueImport.Failed", ex.Message));
        }
    }
}
