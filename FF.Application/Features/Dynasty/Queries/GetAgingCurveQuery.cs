using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Dynasty.Queries;

public record GetAgingCurveQuery(string Position) : IRequest<AgingCurveDocument?>;

public class GetAgingCurveQueryHandler(IAgingCurveRepository repository)
    : IRequestHandler<GetAgingCurveQuery, AgingCurveDocument?>
{
    public Task<AgingCurveDocument?> Handle(GetAgingCurveQuery request, CancellationToken ct)
        => repository.GetByPositionAsync(request.Position, ct);
}