using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Leagues.Commands.SetLeagueVisibility;

public class SetLeagueVisibilityCommandHandler(
    IUserLeaguePreferenceRepository preferenceRepository)
    : IRequestHandler<SetLeagueVisibilityCommand, Result>
{
    public async Task<Result> Handle(
        SetLeagueVisibilityCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await preferenceRepository.GetAsync(
            request.UserId, request.LeagueId, cancellationToken);

        if (existing is null)
        {
            var pref = UserLeaguePreference.Create(
                request.UserId, request.LeagueId, request.IsHidden);
            await preferenceRepository.UpsertAsync(pref, cancellationToken);
        }
        else
        {
            existing.SetVisibility(request.IsHidden);
            await preferenceRepository.UpsertAsync(existing, cancellationToken);
        }

        return Result.Success();
    }
}