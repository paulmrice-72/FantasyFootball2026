using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Features.Matchup;

public record GetMatchupDifficultyQuery(
    string Team,
    string Position,
    int Season,
    int Week) : IRequest<MatchupDifficultyResult?>;


