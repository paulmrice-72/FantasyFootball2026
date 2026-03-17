// FF.Application/Features/Projections/Commands/CalculateProjections/CalculateProjectionsCommand.cs
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Projections.Commands.CalculateProjections;

public record CalculateProjectionsCommand(int Season, int Week) : IRequest<Result<CalculateProjectionsResult>>;

public record CalculateProjectionsResult(
    int ProjectionsCalculated,
    int PlayersSkipped,
    int Season,
    int Week,
    TimeSpan Elapsed);