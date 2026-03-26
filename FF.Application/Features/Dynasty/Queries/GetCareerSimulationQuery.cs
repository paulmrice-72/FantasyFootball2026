using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Dynasty.Queries;

public record GetCareerSimulationQuery(string SleeperPlayerId) : IRequest<CareerSimulationDocument?>;

public class GetCareerSimulationQueryHandler(ICareerSimulationRepository repository)
    : IRequestHandler<GetCareerSimulationQuery, CareerSimulationDocument?>
{
    public Task<CareerSimulationDocument?> Handle(
        GetCareerSimulationQuery request, CancellationToken ct)
        => repository.GetByPlayerIdAsync(request.SleeperPlayerId, ct);
}