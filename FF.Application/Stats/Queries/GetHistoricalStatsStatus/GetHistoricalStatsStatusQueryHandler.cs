// FF.Application/Stats/Queries/GetHistoricalStatsStatus/GetHistoricalStatsStatusQueryHandler.cs

using FF.Application.Interfaces.Persistence;
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Stats.Queries.GetHistoricalStatsStatus;

public class GetHistoricalStatsStatusQueryHandler(IPlayerGameLogRepository gameLogRepository)
        : IRequestHandler<GetHistoricalStatsStatusQuery, Result<HistoricalStatsStatusDto>>
{
    private readonly IPlayerGameLogRepository _gameLogRepository = gameLogRepository;

    public async Task<Result<HistoricalStatsStatusDto>> Handle(
        GetHistoricalStatsStatusQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var counts = await _gameLogRepository.GetDocumentCountsBySeasonAsync(cancellationToken);
            var total = counts.Values.Sum();

            return Result.Success(new HistoricalStatsStatusDto(counts, total, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            return Result.Failure<HistoricalStatsStatusDto>(
                new Error("Stats.StatusQueryFailed", ex.Message));
        }
    }
}