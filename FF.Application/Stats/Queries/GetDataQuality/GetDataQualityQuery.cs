// FF.Application/Stats/Queries/GetDataQuality/GetDataQualityQuery.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Stats.Queries.GetDataQuality;

/// <summary>
/// Returns a full data quality report for all imported PlayerGameLog documents.
/// Handler lives in FF.Infrastructure — requires direct MongoDB access.
/// </summary>
public record GetDataQualityQuery : IRequest<Result<DataQualityReport>>;