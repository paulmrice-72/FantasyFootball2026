using FF.Application.Features.Team.Queries;
using MediatR;

namespace FF.Application.Features.League.Queries.GetOpponentRoster;

public record GetOpponentRosterQuery(
    string SleeperRosterId,
    string SleeperLeagueId) : IRequest<MyRosterDto?>;