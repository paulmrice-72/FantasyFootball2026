// FF.Application/Commands/DetectEmergenceCommand.cs
using MediatR;

namespace FF.Application.Features.EmergenceAlert.Commands;

public record DetectEmergenceCommand(int Season, int Week) : IRequest<DetectEmergenceResult>;

public record DetectEmergenceResult(int PlayersScanned, int AlertsGenerated);