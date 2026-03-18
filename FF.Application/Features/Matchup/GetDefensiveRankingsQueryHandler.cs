using FF.Application.Interfaces.Persistence;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF.Application.Features.Matchup
{
    public class GetDefensiveRankingsQueryHandler(IDefensiveRankingRepository defensiveRankingRepository)
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
                doc.SosAdjustedDifficultyScore,
                doc.SeasonPercentile,
                doc.L4WPercentile,
                doc.AvgFantasyPointsAllowed,
                doc.AvgFantasyPointsAllowedL4W,
                doc.GamesAllowed))];
        }
    }
}

