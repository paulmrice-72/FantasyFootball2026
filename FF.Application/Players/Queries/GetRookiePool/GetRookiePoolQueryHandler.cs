// FF.Application/Players/Queries/GetRookiePool/GetRookiePoolQueryHandler.cs
using FF.Application.Common.Models;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Services;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Players.Queries.GetRookiePool;

public class GetRookiePoolQueryHandler(
    IPlayerRepository playerRepository,
    IDynastyValuationRepository dynastyValuationRepository,
    IFantasyProsRookieRankingRepository fantasyProsRepository)
    : IRequestHandler<GetRookiePoolQuery, Result<List<RookiePlayerDto>>>
{
    private const string HeadshotBaseUrl =
        "https://sleepercdn.com/content/nfl/players/thumb/";

    public async Task<Result<List<RookiePlayerDto>>> Handle(
        GetRookiePoolQuery request,
        CancellationToken cancellationToken)
    {
        // 1 — Rookies from PostgreSQL
        var rookies = await playerRepository.GetRookiesAsync(
            request.Position, cancellationToken);

        if (!rookies.Any())
            return Result<List<RookiePlayerDto>>.Success(new List<RookiePlayerDto>());

        var sleeperIds = rookies
            .Where(p => p.SleeperPlayerId != null)
            .Select(p => p.SleeperPlayerId!)
            .ToList();

        // 2 — Dynasty valuations from MongoDB (batch)
        var valuations = await dynastyValuationRepository
            .GetBySleeperPlayerIdsAsync(sleeperIds, cancellationToken);

        // 3 — FantasyPros rankings from MongoDB (batch)
        var fpRankings = await fantasyProsRepository
            .GetBySleeperPlayerIdsAsync(sleeperIds, cancellationToken);

        // 4 — Join, score, project
        var result = rookies.Select(player =>
        {
            var val = valuations.FirstOrDefault(
                v => v.SleeperPlayerId == player.SleeperPlayerId);

            var fp = fpRankings.FirstOrDefault(
                r => r.SleeperPlayerId == player.SleeperPlayerId);

            var overallPick = player.DraftRound.HasValue && player.DraftPick.HasValue
                ? ((player.DraftRound.Value - 1) * 32) + player.DraftPick.Value
                : (int?)null;

            var breakdown = RookieDynastyScoreCalculator.CalculateWithBreakdown(
                overallPick: overallPick,
                position: player.Position.ToString(),
                valuation: val,
                fantasyProsRank: fp?.FantasyProsRank);

            // Headshot: Sleeper CDN — returns a placeholder image if not found
            var headshotUrl = player.SleeperPlayerId is not null
                ? $"{HeadshotBaseUrl}{player.SleeperPlayerId}.jpg"
                : null;

            return new RookiePlayerDto(
                SleeperPlayerId: player.SleeperPlayerId ?? string.Empty,
                FullName: player.FullName,
                Position: player.Position.ToString(),
                NflTeam: player.NflTeam,
                Age: player.Age,
                DraftRound: player.DraftRound,
                DraftPick: player.DraftPick,
                CollegeTeam: player.CollegeTeam,
                HeadshotUrl: headshotUrl,
                CareerValueScore: val?.CareerValueScore,
                TradeValue: val?.TradeValue,
                DiscountedFutureValue: val?.DiscountedFutureValue,
                BreakoutScore: val?.BreakoutScore,
                FantasyProsRank: fp?.FantasyProsRank,
                FantasyProsPositionRank: fp?.PositionRank,
                FantasyProsTier: fp?.Tier,
                DynastyScore: breakdown.DynastyScore,
                DraftCapitalScore: breakdown.DraftCapitalScore,
                PositionalScore: breakdown.PositionalScore,
                ValuationBlendScore: breakdown.ValuationBlendScore,
                FantasyProsScore: breakdown.FantasyProsScore
            );
        })
        .OrderByDescending(r => r.DynastyScore)
        .ThenBy(r => r.FantasyProsRank ?? 999)
        .ToList();

        return Result<List<RookiePlayerDto>>.Success(result);
    }
}