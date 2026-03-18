using FF.Application.Interfaces.Services;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF.Application.Features.Matchup
{
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
    }
}