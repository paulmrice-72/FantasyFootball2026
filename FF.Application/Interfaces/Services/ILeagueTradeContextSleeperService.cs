// FF.Application/Interfaces/Services/ILeagueTradeContextSleeperService.cs
using FF.Application.Features.Trade.Queries.GetLeagueTradeContext;

namespace FF.Application.Interfaces.Services;

/// <summary>
/// Fetches pick data from Sleeper for the League Trade Analyzer.
/// </summary>
public interface ILeagueTradeContextSleeperService
{
    /// <summary>
    /// Returns all picks that have changed hands (traded picks) for future seasons.
    /// roster_id = original owner, owner_id = current owner.
    /// </summary>
    Task<List<TradedPickInfo>> GetTradedPicksAsync(
        string leagueId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a lookup of (round, rosterId) → draft slot number within the round
    /// for the current season's rookie draft. E.g. (1, "5") → 7 means that roster
    /// owns pick 1.07. Returns empty dict if draft order hasn't been set yet.
    /// </summary>
    Task<Dictionary<(int Round, string RosterId), int>> GetCurrentSeasonPickSlotsAsync(
        string leagueId,
        int season,
        CancellationToken ct = default);
}