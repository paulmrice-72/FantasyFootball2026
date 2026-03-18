// FF.Application/Features/Projections/Queries/GetWeightProfile/GetWeightProfileQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.ValueObjects;
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Projections.Queries.GetWeightProfile;

public class GetWeightProfileQueryHandler(
    IUserProjectionWeightProfileRepository repo)
    : IRequestHandler<GetWeightProfileQuery, Result<WeightProfileDto>>
{
    public async Task<Result<WeightProfileDto>> Handle(
        GetWeightProfileQuery request, CancellationToken ct)
    {
        var profile = await repo.GetActiveByUserAsync(request.AppUserId, ct);

        var defaults = ProjectionWeightProfile.Default;

        return Result.Success(new WeightProfileDto(
            profile?.RecentGameWeight ?? defaults.RecentGameWeight,
            profile?.SnapCountWeight ?? defaults.SnapCountWeight,
            profile?.MatchupWeight ?? defaults.MatchupWeight,
            profile?.MinGamesRequired ?? defaults.MinGamesRequired,
            profile?.LookbackWeeks ?? defaults.LookbackWeeks,
            profile?.ProfileName ?? "Default"));
    }
}