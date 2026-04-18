// FF.Application/Interfaces/Services/ILeagueTradeContextSleeperService.cs
using FF.Application.Features.Trade.Queries.GetLeagueTradeContext;

namespace FF.Application.Interfaces.Services;

/// <summary>
/// Abstraction over the Sleeper traded-picks endpoint.
/// Keeps the Refit/HTTP concern in Infrastructure and the handler clean.
/// </summary>
public interface ILeagueTradeContextSleeperService
{
    Task<List<TradedPickInfo>> GetTradedPicksAsync(
        string leagueId,
        CancellationToken ct = default);
}
