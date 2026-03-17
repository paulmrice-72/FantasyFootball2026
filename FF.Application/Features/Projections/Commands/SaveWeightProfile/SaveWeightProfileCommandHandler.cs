// FF.Application/Features/Projections/Commands/SaveWeightProfile/SaveWeightProfileCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Projections.Commands.SaveWeightProfile;

public class SaveWeightProfileCommandHandler(
    IUserProjectionWeightProfileRepository repo)
    : IRequestHandler<SaveWeightProfileCommand, Result>
{
    public async Task<Result> Handle(
        SaveWeightProfileCommand request, CancellationToken ct)
    {
        var existing = await repo.GetActiveByUserAsync(request.AppUserId, ct);

        if (existing is null)
        {
            var newProfile = UserProjectionWeightProfile.CreateDefault(request.AppUserId);
            newProfile.Update(
                request.ProfileName,
                request.RecentGameWeight,
                request.SnapCountWeight,
                request.MatchupWeight,
                request.MinGamesRequired,
                request.LookbackWeeks);
            await repo.UpsertAsync(newProfile, ct);
        }
        else
        {
            existing.Update(
                request.ProfileName,
                request.RecentGameWeight,
                request.SnapCountWeight,
                request.MatchupWeight,
                request.MinGamesRequired,
                request.LookbackWeeks);
            await repo.UpsertAsync(existing, ct);
        }

        return Result.Success();
    }
}