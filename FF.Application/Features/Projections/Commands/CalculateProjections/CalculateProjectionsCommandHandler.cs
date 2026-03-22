// FF.Application/Features/Projections/Commands/CalculateProjections/CalculateProjectionsCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.ValueObjects;
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FF.Application.Features.Projections.Commands.CalculateProjections;

public class CalculateProjectionsCommandHandler(
    IPlayerGameLogRepository gameLogRepository,
    IPlayerProjectionRepository projectionRepository,
    ProjectionInputBuilder inputBuilder,
    IVegasLineRepository vegasLineRepository,
    ILogger<CalculateProjectionsCommandHandler> logger)
    : IRequestHandler<CalculateProjectionsCommand, Result<CalculateProjectionsResult>>
{
    private static readonly string[] SupportedPositions = ["QB", "RB", "WR", "TE"];

    public async Task<Result<CalculateProjectionsResult>> Handle(
        CalculateProjectionsCommand request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var calculated = 0;
        var skipped = 0;

        logger.LogInformation("Starting projection calculation for Season {Season} Week {Week}",
            request.Season, request.Week);

        // Get distinct players who have game logs for this season
        var playerIds = await gameLogRepository.GetDistinctPlayerIdsAsync(
            request.Season, cancellationToken);

        logger.LogInformation("Found {Count} players with game logs for {Season}",
            playerIds.Count, request.Season);


        var countByPosition = new Dictionary<string, int>
        {
            ["QB"] = 0,
            ["RB"] = 0,
            ["WR"] = 0,
            ["TE"] = 0
        };
        var skipByPosition = new Dictionary<string, int>
        {
            ["QB"] = 0,
            ["RB"] = 0,
            ["WR"] = 0,
            ["TE"] = 0
        };

        foreach (var playerId in playerIds)
        {
            try
            {
                // Get the most recent log to determine position/team/opponent
                var recentLog = await gameLogRepository.GetMostRecentAsync(
                    playerId, request.Season, request.Week, cancellationToken);

                if (recentLog is null)
                {
                    skipped++;
                    continue;
                }

                if (!SupportedPositions.Contains(recentLog.Position))
                {
                    skipped++;
                    continue;
                }

                var position = recentLog.Position;

                var input = await inputBuilder.BuildAsync(
                    playerId,
                    recentLog.Position,
                    recentLog.OpponentTeam ?? "UNK",   // ← was hardcoded "UNK"
                    request.Season,
                    request.Week,
                    ProjectionWeightProfile.Default,
                    cancellationToken);

                if (input is null)
                {
                    skipped++;
                    skipByPosition[position] = skipByPosition.GetValueOrDefault(position) + 1;
                    logger.LogDebug("Skipped {PlayerId} {Position} - insufficient input", playerId, position);
                    continue;
                }

                var result = PlayerProjectionService.Project(input);

                if (result.IsInsufficient)
                {
                    skipped++;
                    skipByPosition[position] = skipByPosition.GetValueOrDefault(position) + 1;
                    logger.LogDebug("Skipped {PlayerId} {Position} - insufficient regression data", playerId, position);
                    continue;
                }

                var vegasLine = await vegasLineRepository.GetByTeamAsync(
                    recentLog.NflTeam, request.Season, request.Week, cancellationToken);

                var spread = vegasLine is not null
                    ? recentLog.NflTeam == vegasLine.HomeTeam
                        ? vegasLine.HomeSpread
                        : vegasLine.AwaySpread
                    : 0m;  // fallback: no line posted yet → Competitive/neutral

                var correlation = GameScriptClassifier.Classify(spread);
                var doc = MapToDocument(result, recentLog, request.Season, request.Week, correlation);

                await projectionRepository.UpsertAsync(doc, cancellationToken);
                calculated++;
                countByPosition[position] = countByPosition.GetValueOrDefault(position) + 1;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to calculate projection for player {PlayerId}", playerId);
                skipped++;
            }
        }

        sw.Stop();

        return Result.Success(new CalculateProjectionsResult(
            calculated, skipped, request.Season, request.Week, sw.Elapsed));
    }

    private static PlayerProjectionDocument MapToDocument(
        PlayerProjectionResult result,
        PlayerGameLogDocument recentLog,
        int season,
        int week,
        FF.Domain.ValueObjects.CorrelationMetadata correlation)
    {
        // Apply game script volume multiplier to projections
        var adjustedPpr = GameScriptClassifier.ApplyMultiplier(
                                  result.ProjectedPointsPpr, recentLog.Position, correlation);
        var adjustedHalfPpr = GameScriptClassifier.ApplyMultiplier(
                                  result.ProjectedPointsHalfPpr, recentLog.Position, correlation);
        var adjustedStd = GameScriptClassifier.ApplyMultiplier(
                                  result.ProjectedPoints, recentLog.Position, correlation);

        return new PlayerProjectionDocument
        {
            PlayerId = result.PlayerId,
            SleeperPlayerId = recentLog.SleeperPlayerId ?? string.Empty,
            PlayerName = recentLog.PlayerName,
            Position = recentLog.Position,
            NflTeam = recentLog.NflTeam,
            OpponentTeam = recentLog.OpponentTeam ?? "UNK",
            Season = season,
            Week = week,
            ProjectedPoints = adjustedStd,
            ProjectedPointsPpr = adjustedPpr,
            ProjectedPointsHalfPpr = adjustedHalfPpr,
            WeightedAvgPoints = result.WeightedAvgPoints,
            MatchupAdjustmentFactor = result.MatchupAdjustmentFactor,
            SnapPctInput = result.SnapPctInput,
            TargetShareInput = result.TargetShareInput,
            GameSampleSize = result.GameSampleSize,
            RSquared = result.RSquared,
            ScoringFormat = "HalfPpr",
            GameScript = correlation.Script.ToString(),
            RbVolumeMultiplier = correlation.RbVolumeMultiplier,
            WrTeVolumeMultiplier = correlation.WrTeVolumeMultiplier,
            SpreadInput = correlation.Spread,
            CalculatedAt = DateTime.UtcNow
        };
    }
}