using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Features.Matchup
{
    public class GetMatchupDifficultyQueryHandler(
        IDefensiveRankingRepository defensiveRankingRepository)
        : IRequestHandler<GetMatchupDifficultyQuery, MatchupDifficultyResult?>
    {
        public async Task<MatchupDifficultyResult?> Handle(
            GetMatchupDifficultyQuery request,
            CancellationToken cancellationToken)
        {
            var doc = await defensiveRankingRepository.GetAsync(
                request.Team,
                request.Position,
                request.Season,
                request.Week,
                cancellationToken);

            if (doc is null) return null;

            return new MatchupDifficultyResult(
                doc.Team,
                doc.Position,
                doc.Season,
                doc.Week,
                doc.DifficultyScore,
                doc.SosAdjustedDifficultyScore,            // ← new
                doc.SeasonPercentile,
                doc.L4WPercentile,
                doc.AvgFantasyPointsAllowed,
                doc.AvgFantasyPointsAllowedL4W,
                doc.GamesAllowed);
        }
    }
}