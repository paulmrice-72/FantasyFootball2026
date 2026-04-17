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
    IFantasyProsRookieRankingRepository fantasyProsRepository,
    IPffDraftGradeRepository pffRepository,
    IConsensusAdpRepository adpRepository)
    : IRequestHandler<GetRookiePoolQuery, Result<List<RookiePlayerDto>>>
{
    private const string HeadshotBaseUrl = "https://sleepercdn.com/content/nfl/players/thumb/";

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

        // 2 — All signal sources from MongoDB (batch, parallel)
        var valuationsTask = dynastyValuationRepository.GetBySleeperPlayerIdsAsync(sleeperIds, cancellationToken);
        var fpTask = fantasyProsRepository.GetBySleeperPlayerIdsAsync(sleeperIds, cancellationToken);
        var pffTask = pffRepository.GetBySleeperPlayerIdsAsync(sleeperIds, cancellationToken);
        var adpTask = adpRepository.GetBySleeperPlayerIdsAsync(sleeperIds, cancellationToken);

        await Task.WhenAll(valuationsTask, fpTask, pffTask, adpTask);

        var valuations = await valuationsTask;
        var fpRankings = await fpTask;
        var pffGrades = await pffTask;
        var adpData = await adpTask;

        // 3 — Join, score, project
        var result = rookies.Select(player =>
        {
            var val = valuations.FirstOrDefault(v => v.SleeperPlayerId == player.SleeperPlayerId);
            var fp = fpRankings.FirstOrDefault(r => r.SleeperPlayerId == player.SleeperPlayerId);
            var pff = pffGrades.FirstOrDefault(g => g.SleeperPlayerId == player.SleeperPlayerId);
            var adp = adpData.FirstOrDefault(a => a.SleeperPlayerId == player.SleeperPlayerId);

            var overallPick = player.DraftRound.HasValue && player.DraftPick.HasValue
                ? ((player.DraftRound.Value - 1) * 32) + player.DraftPick.Value
                : (int?)null;

            var breakdown = RookieDynastyScoreCalculator.CalculateWithBreakdown(
                overallPick: overallPick,
                position: player.Position.ToString(),
                valuation: val,
                fantasyProsRank: fp?.FantasyProsRank,
                pffGrade: pff?.PffGrade,
                consensusAdp: adp?.Adp);

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
                PffGrade: pff?.PffGrade,
                PffRank: pff?.PffRank,
                ConsensusAdp: adp?.Adp,
                ConsensusAdpRank: adp?.AdpRank,
                AdpSource: adp?.Source,
                DynastyScore: breakdown.DynastyScore,
                DraftCapitalScore: breakdown.DraftCapitalScore,
                FantasyProsScore: breakdown.FantasyProsScore,
                PffGradeScore: breakdown.PffGradeScore,
                ConsensusAdpScore: breakdown.ConsensusAdpScore,
                ValuationBlendScore: breakdown.ValuationBlendScore,
                PositionalScore: breakdown.PositionalScore,
                ActiveSignals: breakdown.ActiveSignals);
        })
        .OrderByDescending(r => r.DynastyScore)
        .ThenBy(r => r.FantasyProsRank ?? 999)
        .ToList();

        return Result<List<RookiePlayerDto>>.Success(result);
    }
}