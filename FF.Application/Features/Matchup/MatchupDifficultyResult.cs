using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF.Application.Features.Matchup
{
    public record MatchupDifficultyResult(
        string Team,
        string Position,
        int Season,
        int Week,
        decimal DifficultyScore,       // 0-100
        decimal SeasonPercentile,
        decimal L4WPercentile,
        decimal AvgPointsAllowed,
        decimal AvgPointsAllowedL4W,
        int GamesAllowed);
}
