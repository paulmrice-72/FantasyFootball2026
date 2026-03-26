using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Dynasty.Queries;

public record GetDynastyValuationsQuery(
    string? Position = null,
    int TopCount = 50) : IRequest<List<DynastyValuationDocument>>;

public class GetDynastyValuationsQueryHandler(IDynastyValuationRepository repository)
    : IRequestHandler<GetDynastyValuationsQuery, List<DynastyValuationDocument>>
{
    public Task<List<DynastyValuationDocument>> Handle(
        GetDynastyValuationsQuery request, CancellationToken ct)
        => repository.GetTopByTradeValueAsync(request.TopCount, request.Position, ct);
}