// FF.Application/Players/Queries/GetPlayerNarrative/GetPlayerNarrativeQueryHandler.cs
using FF.Application.Common.Models;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Players.Queries.GetPlayerNarrative;

public class GetPlayerNarrativeQueryHandler(
    IPlayerRepository playerRepository,
    IPlayerNarrativeRepository narrativeRepository,
    IDynastyValuationRepository dynastyValuationRepository,
    IFantasyProsRookieRankingRepository fantasyProsRepository,
    IPlayerScoutService scoutService)
    : IRequestHandler<GetPlayerNarrativeQuery, Result<PlayerNarrativeDto>>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    public async Task<Result<PlayerNarrativeDto>> Handle(
        GetPlayerNarrativeQuery request,
        CancellationToken cancellationToken)
    {
        // 1 — Check cache first
        var cached = await narrativeRepository.GetBySleeperPlayerIdAsync(
            request.SleeperPlayerId, cancellationToken);

        if (cached is not null && cached.ExpiresAt > DateTime.UtcNow)
            return Result<PlayerNarrativeDto>.Success(
                new PlayerNarrativeDto(cached.Narrative, FromCache: true));

        // 2 — Load player from PostgreSQL
        var players = await playerRepository.GetRookiesAsync(null, cancellationToken);
        var player = players.FirstOrDefault(
            p => p.SleeperPlayerId == request.SleeperPlayerId);

        if (player is null)
            return Result<PlayerNarrativeDto>.Failure(
                new Error("Player.NotFound", $"Player {request.SleeperPlayerId} not found."));

        // 3 — Load supporting data from MongoDB
        var ids = new List<string> { request.SleeperPlayerId };

        var valuations = await dynastyValuationRepository
            .GetBySleeperPlayerIdsAsync(ids, cancellationToken);
        var val = valuations.Count > 0 ? valuations[0] : null;

        var fpRankings = await fantasyProsRepository
            .GetBySleeperPlayerIdsAsync(ids, cancellationToken);
        var fp = fpRankings.Count > 0 ? fpRankings[0] : null;

        var overallPick = player.DraftRound.HasValue && player.DraftPick.HasValue
            ? ((player.DraftRound.Value - 1) * 32) + player.DraftPick.Value
            : (int?)null;

        var breakdown = RookieDynastyScoreCalculator.CalculateWithBreakdown(
            overallPick: overallPick,
            position: player.Position.ToString(),
            valuation: val,
            fantasyProsRank: fp?.FantasyProsRank,
            pffGrade: null,
            consensusAdp: null);

        // 4 — Generate via Anthropic
        var narrative = await scoutService.GeneratePlayerNarrativeAsync(
            sleeperPlayerId: request.SleeperPlayerId,
            fullName: player.FullName,
            position: player.Position.ToString(),
            nflTeam: player.NflTeam,
            age: player.Age,
            collegeTeam: player.CollegeTeam,
            draftRound: player.DraftRound,
            draftPick: player.DraftPick,
            dynastyScore: breakdown.DynastyScore,
            draftCapitalScore: breakdown.DraftCapitalScore,
            positionalScore: breakdown.PositionalScore,
            valuationBlendScore: breakdown.ValuationBlendScore,
            fantasyProsScore: breakdown.FantasyProsScore,
            fantasyProsRank: fp?.FantasyProsRank,
            ct: cancellationToken);

        if (narrative is null)
            return Result<PlayerNarrativeDto>.Failure(
                new Error("Narrative.GenerationFailed", "Could not generate scout report."));

        // 5 — Cache to MongoDB
        var doc = new PlayerNarrativeDocument
        {
            SleeperPlayerId = request.SleeperPlayerId,
            FullName = player.FullName,
            Position = player.Position.ToString(),
            Narrative = narrative,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(CacheTtl)
        };

        await narrativeRepository.UpsertAsync(doc, cancellationToken);

        return Result<PlayerNarrativeDto>.Success(
            new PlayerNarrativeDto(narrative, FromCache: false));
    }
}