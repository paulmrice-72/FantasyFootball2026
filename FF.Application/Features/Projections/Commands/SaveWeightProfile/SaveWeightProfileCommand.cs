// FF.Application/Features/Projections/Commands/SaveWeightProfile/SaveWeightProfileCommand.cs
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Projections.Commands.SaveWeightProfile;

public record SaveWeightProfileCommand(
    string AppUserId,
    string ProfileName,
    decimal RecentGameWeight,
    decimal SnapCountWeight,
    decimal MatchupWeight,
    int MinGamesRequired,
    int LookbackWeeks) : IRequest<Result>;