// FF.Application/Features/Simulations/Commands/RunSimulations/RunSimulationsCommand.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Simulations.Commands.RunSimulations;

public record RunSimulationsCommand(int Season, int Week) : IRequest<Result<RunSimulationsResult>>;

public record RunSimulationsResult(
    int Simulated,
    int Skipped,
    int Season,
    int Week,
    TimeSpan Elapsed);