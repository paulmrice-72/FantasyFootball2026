using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Dynasty.Commands;

public record RunCareerSimulationsCommand(int Season) : IRequest<RunCareerSimulationsResult>;

public record RunCareerSimulationsResult(int Simulated, int Failed, TimeSpan Elapsed);

public class RunCareerSimulationsCommandHandler(
    ICareerSimulationService simulationService,
    ICareerSimulationRepository repository)
    : IRequestHandler<RunCareerSimulationsCommand, RunCareerSimulationsResult>
{
    public async Task<RunCareerSimulationsResult> Handle(
        RunCareerSimulationsCommand request, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var simulations = await simulationService.SimulateAllPlayersAsync(request.Season, ct);
        await repository.UpsertBatchAsync(simulations, ct);

        sw.Stop();
        return new RunCareerSimulationsResult(simulations.Count, 0, sw.Elapsed);
    }
}