using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using MediatR;

namespace FF.Application.Features.Matchup;

public record CalculateDefensiveRankingsCommand(
    int Season,
    int ThroughWeek) : IRequest<CalculateDefensiveRankingsResult>;

public record CalculateDefensiveRankingsResult(bool Success, string? ErrorMessage);

public class CalculateDefensiveRankingsCommandHandler(
    IDefensiveRankingService defensiveRankingService)
    : IRequestHandler<CalculateDefensiveRankingsCommand, CalculateDefensiveRankingsResult>
{
    public async Task<CalculateDefensiveRankingsResult> Handle(
        CalculateDefensiveRankingsCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await defensiveRankingService.CalculateAsync(
                request.Season,
                request.ThroughWeek,
                cancellationToken);

            return new CalculateDefensiveRankingsResult(true, null);
        }
        catch (Exception ex)
        {
            return new CalculateDefensiveRankingsResult(false, ex.Message);
        }
    }

    public record GetDefensiveRankingsQuery(
    int Season,
    int Week) : IRequest<List<MatchupDifficultyResult>>;

    public class GetDefensiveRankingsQueryHandler(
        IDefensiveRankingRepository defensiveRankingRepository)
        : IRequestHandler<GetDefensiveRankingsQuery, List<MatchupDifficultyResult>>
    {
        public async Task<List<MatchupDifficultyResult>> Handle(
            GetDefensiveRankingsQuery request,
            CancellationToken cancellationToken)
        {
            var docs = await defensiveRankingRepository.GetByWeekAsync(
                request.Season,
                request.Week,
                cancellationToken);

            return [.. docs.Select(doc => new MatchupDifficultyResult(
                doc.Team,
                doc.Position,
                doc.Season,
                doc.Week,
                doc.DifficultyScore,
                doc.SeasonPercentile,
                doc.L4WPercentile,
                doc.AvgFantasyPointsAllowed,
                doc.AvgFantasyPointsAllowedL4W,
                doc.GamesAllowed))];
        }
    }
}