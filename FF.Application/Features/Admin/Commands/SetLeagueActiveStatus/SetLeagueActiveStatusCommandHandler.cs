using FF.Application.Interfaces.Persistence;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Admin.Commands.SetLeagueActiveStatus;

public class SetLeagueActiveStatusCommandHandler(
    ILeagueRepository leagueRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<SetLeagueActiveStatusCommand, Result>
{
    public async Task<Result> Handle(
        SetLeagueActiveStatusCommand request,
        CancellationToken cancellationToken)
    {
        var league = await leagueRepository.GetByIdAsync(request.LeagueId, cancellationToken);
        if (league is null)
            return Result.Failure(Error.NotFound("League.NotFound", $"League {request.LeagueId} not found."));

        league.SetActiveStatus(request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}