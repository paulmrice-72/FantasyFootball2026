using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Dynasty.Queries;

public record GetTradeHistoryQuery(string UserId) : IRequest<List<TradeAnalysisDocument>>;

public class GetTradeHistoryQueryHandler(ITradeAnalysisRepository repository)
    : IRequestHandler<GetTradeHistoryQuery, List<TradeAnalysisDocument>>
{
    public Task<List<TradeAnalysisDocument>> Handle(
        GetTradeHistoryQuery request, CancellationToken ct)
        => repository.GetByUserIdAsync(request.UserId, ct);
}