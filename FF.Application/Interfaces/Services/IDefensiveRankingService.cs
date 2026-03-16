using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF.Application.Interfaces.Services
{
    public interface IDefensiveRankingService
    {
        Task CalculateAsync(int season, int throughWeek, CancellationToken ct = default);
    }
}
