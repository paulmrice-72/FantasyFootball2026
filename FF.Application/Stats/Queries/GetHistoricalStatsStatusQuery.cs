// FF.Application/Stats/Queries/GetHistoricalStatsStatus/GetHistoricalStatsStatusQuery.cs

using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Stats.Queries.GetHistoricalStatsStatus;

/// <summary>
/// Returns document counts per season from MongoDB PlayerGameLogs collection.
/// Used by GET /api/v1/stats/status to verify import completeness.
/// Handler lives in FF.Infrastructure (GetHistoricalStatsStatusQueryHandler)
/// because it requires direct MongoDB repository access.
/// </summary>
public record GetHistoricalStatsStatusQuery : IRequest<Result<HistoricalStatsStatusDto>>;

public record HistoricalStatsStatusDto(
    Dictionary<int, long> DocumentCountsBySeason,
    long TotalDocuments,
    DateTime QueriedAt
);