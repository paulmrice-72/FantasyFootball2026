// FF.Application/Features/DraftTools/Queries/GetRedraftBoard/GetRedraftBoardQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Queries.GetRedraftBoard;

public class GetRedraftBoardQueryHandler(
    IRedraftAdpRepository redraftAdpRepository,
    ISimulationResultRepository simulationResultRepository,
    ILogger<GetRedraftBoardQueryHandler> logger)
    : IRequestHandler<GetRedraftBoardQuery, Result<List<RedraftBoardEntryDto>>>
{
    public async Task<Result<List<RedraftBoardEntryDto>>> Handle(
        GetRedraftBoardQuery request,
        CancellationToken cancellationToken)
    {
        var adpEntries = await redraftAdpRepository.GetBySeasonAsync(
            request.Season, "ppr", cancellationToken);

        if (adpEntries.Count == 0)
        {
            logger.LogWarning(
                "GetRedraftBoard: no FFC ADP cached for season {Season} — " +
                "run POST /api/v1/admin/sync-ffc-adp first.", request.Season);
        }

        // Prior-season per-game average — season-average sentinel is Week=0.
        // Empty until SeedSeasonAverageSimsCommand has been run for Season-1;
        // that's fine, SeasonAvgPoints just comes back null for everyone.
        var seasonAvgResults = await simulationResultRepository.GetByWeekAsync(
            request.Season - 1, 0, cancellationToken);

        var avgBySleeperId = seasonAvgResults
            .Where(s => !string.IsNullOrEmpty(s.SleeperPlayerId))
            .GroupBy(s => s.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.First().Median);

        IEnumerable<RedraftAdpCacheDocument> query =
            adpEntries.Where(a => !string.IsNullOrEmpty(a.SleeperPlayerId));

        if (!string.IsNullOrWhiteSpace(request.Position))
        {
            query = query.Where(a =>
                string.Equals(a.Position, request.Position, StringComparison.OrdinalIgnoreCase));
        }

        var board = query
            .Select(a => new RedraftBoardEntryDto(
                a.SleeperPlayerId,
                a.PlayerName,
                a.Position,
                a.NflTeam,
                a.Adp,
                a.AdpRound,
                avgBySleeperId.TryGetValue(a.SleeperPlayerId, out var avg) ? avg : (decimal?)null))
            .OrderBy(e => e.Adp)
            .ToList();

        return Result.Success(board);
    }
}
