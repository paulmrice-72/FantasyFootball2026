using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF.Application.Features.Matchup
{
    public record GetDefensiveRankingsQuery(int Season, int Week) : IRequest<List<MatchupDifficultyResult>>;
}
