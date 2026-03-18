using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using MediatR;

namespace FF.Application.Features.Matchup;

public record CalculateDefensiveRankingsCommand(
    int Season,
    int ThroughWeek) : IRequest<CalculateDefensiveRankingsResult>;

public record CalculateDefensiveRankingsResult(bool Success, string? ErrorMessage);
