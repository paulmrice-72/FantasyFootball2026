// FF.Application/Features/Projections/Queries/GetWeightProfile/GetWeightProfileQuery.cs
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Projections.Queries.GetWeightProfile;

public record GetWeightProfileQuery(string AppUserId)
    : IRequest<Result<WeightProfileDto>>;

public record WeightProfileDto(
    decimal RecentGameWeight,
    decimal SnapCountWeight,
    decimal MatchupWeight,
    int MinGamesRequired,
    int LookbackWeeks,
    string ProfileName);