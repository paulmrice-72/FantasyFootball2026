// FF.Application/Players/Commands/BackfillCollegeTeam/BackfillCollegeTeamCommand.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Players.Commands.BackfillCollegeTeam;

public record BackfillCollegeTeamCommand(string CsvContent)
    : IRequest<Result<BackfillCollegeTeamResult>>;

public record BackfillCollegeTeamResult(
    int PlayersUpdated,
    int PlayersSkipped,
    int UnmatchedInCsv);