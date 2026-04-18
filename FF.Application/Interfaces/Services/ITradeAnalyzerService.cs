// FF.Application/Interfaces/Services/ITradeAnalyzerService.cs
using FF.Application.Features.Dynasty.Commands;
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Services;

public interface ITradeAnalyzerService
{
    /// <summary>Generic trade analysis — no league context.</summary>
    Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        IEnumerable<TradePickRequest> myPicks,
        IEnumerable<TradePickRequest> theirPicks,
        int season,
        CancellationToken ct = default);

    /// <summary>
    /// League-aware trade analysis — adds roster composition,
    /// drop impact, and league standing scoring dimensions.
    /// </summary>
    Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        IEnumerable<TradePickRequest> myPicks,
        IEnumerable<TradePickRequest> theirPicks,
        int season,
        string? leagueId,
        string? sleeperUserId,
        CancellationToken ct = default);
}
