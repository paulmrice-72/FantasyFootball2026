// FF.Application/Features/Projections/Commands/CalculateProjections/CalculateProjectionsCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.ValueObjects;
using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FF.Application.Features.Projections.Commands.CalculateProjections;

/// <summary>
/// Orchestrates a projection run (Epic 20 / FAN-116).
///
/// Two passes:
///
/// * **Pass 1 — players with history.** L0 regresses a stat line from game logs;
///   L1 scores it into the three cached formats.
/// * **Pass 2 — rookies.** No game logs exist, so a prior is built from depth
///   chart position, combine athleticism and (bounded) consensus rank. Without
///   this pass a rookie has no projection at all and renders as a silent zero in
///   roster grades — the original complaint that started Epic 20.
///
/// The handler stays thin: it resolves WHICH season the run can be built from,
/// then hands each player to a pure projection service.
///
/// The basis resolution is the important part of pass 1. Previously this handler
/// asked for the requested season's player list, got nothing back in preseason,
/// wrote no projections at all, and left every downstream reader to silently fall
/// back to last season inside the repository. Now the fallback is decided once, up
/// front, recorded on every document it produces, and reported in the result.
/// </summary>
public class CalculateProjectionsCommandHandler(
    IPlayerGameLogRepository gameLogRepository,
    IPlayerProjectionRepository projectionRepository,
    ProjectionInputBuilder inputBuilder,
    IVegasLineRepository vegasLineRepository,
    IPlayerRepository playerRepository,
    IDepthChartRepository depthChartRepository,
    IFantasyProsRookieRankingRepository rookieRankingRepository,
    ICombineResultRepository combineResultRepository,
    ILogger<CalculateProjectionsCommandHandler> logger)
    : IRequestHandler<CalculateProjectionsCommand, Result<CalculateProjectionsResult>>
{
    private static readonly string[] SupportedPositions = ["QB", "RB", "WR", "TE"];

    // Sentinel "week" used to mean "the last game of that season" when reading the
    // most recent log from a carryover season.
    private const int EndOfSeasonWeek = 100;

    public async Task<Result<CalculateProjectionsResult>> Handle(
        CalculateProjectionsCommand request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var calculated = 0;
        var skipped = 0;

        // ── Resolve the basis season ──────────────────────────────────────
        var counts = await gameLogRepository.GetDocumentCountsBySeasonAsync(cancellationToken);

        var (basis, basisSeason) = ResolveBasis(counts, request.Season);

        if (basis == ProjectionBasis.None)
        {
            logger.LogWarning(
                "No game logs available for Season {Season} or {PriorSeason} — " +
                "nothing to project from history. Run the stats sync before projecting.",
                request.Season, request.Season - 1);
        }
        else if (basis == ProjectionBasis.PriorSeasonCarryover)
        {
            logger.LogWarning(
                "Season {Season} has no game logs — projecting Week {Week} from " +
                "{BasisSeason} carryover. Every value produced by this pass is " +
                "prior-season data and is stamped Basis=PriorSeasonCarryover.",
                request.Season, request.Week, basisSeason);
        }

        logger.LogInformation(
            "Starting projection calculation for Season {Season} Week {Week} " +
            "(basis {Basis}, basis season {BasisSeason})",
            request.Season, request.Week, basis, basisSeason);

        // ── Pre-load Vegas lines once, not per player ─────────────────────
        var vegasLines = await vegasLineRepository.GetByWeekAsync(
            request.Season, request.Week, cancellationToken);

        var spreadByTeam = vegasLines
            .SelectMany(v => new[]
            {
                (Team: v.HomeTeam, Spread: v.HomeSpread),
                (Team: v.AwayTeam, Spread: v.AwaySpread)
            })
            .GroupBy(x => x.Team, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Spread, StringComparer.OrdinalIgnoreCase);

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

        // Tracks who pass 1 covered, so pass 2 never double-projects a player.
        var projectedSleeperIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Pass 1 — players with game logs ───────────────────────────────
        if (basis != ProjectionBasis.None)
        {
            var playerIds = await gameLogRepository.GetDistinctPlayerIdsAsync(
                basisSeason, cancellationToken);

            logger.LogInformation("Found {Count} players with game logs for {BasisSeason}",
                playerIds.Count, basisSeason);

            var lookupWeek = basis == ProjectionBasis.CurrentSeason
                ? request.Week
                : EndOfSeasonWeek;

            foreach (var playerId in playerIds)
            {
                try
                {
                    var recentLog = await gameLogRepository.GetMostRecentAsync(
                        playerId, basisSeason, lookupWeek, cancellationToken);

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

                    // No line posted (or preseason) → spread 0 → neutral Competitive script.
                    var spread = spreadByTeam.TryGetValue(recentLog.NflTeam, out var s) ? s : 0m;
                    var correlation = GameScriptClassifier.Classify(spread);

                    var input = await inputBuilder.BuildStatLineInputAsync(
                        playerId,
                        position,
                        recentLog.OpponentTeam ?? "UNK",
                        request.Season,
                        request.Week,
                        basis,
                        basisSeason,
                        correlation,
                        ProjectionWeightProfile.Default,
                        cancellationToken);

                    if (input is null)
                    {
                        skipped++;
                        skipByPosition[position] = skipByPosition.GetValueOrDefault(position) + 1;
                        logger.LogDebug("Skipped {PlayerId} {Position} — no usable game logs", playerId, position);
                        continue;
                    }

                    var projection = StatLineProjectionService.Project(input);

                    if (projection.IsInsufficient)
                    {
                        skipped++;
                        skipByPosition[position] = skipByPosition.GetValueOrDefault(position) + 1;
                        logger.LogDebug("Skipped {PlayerId} {Position} — insufficient sample", playerId, position);
                        continue;
                    }

                    var doc = MapToDocument(projection, recentLog, request.Season, request.Week, correlation);

                    await projectionRepository.UpsertAsync(doc, cancellationToken);
                    calculated++;
                    countByPosition[position] = countByPosition.GetValueOrDefault(position) + 1;

                    if (!string.IsNullOrEmpty(doc.SleeperPlayerId))
                        projectedSleeperIds.Add(doc.SleeperPlayerId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to calculate projection for player {PlayerId}", playerId);
                    skipped++;
                }
            }
        }

        // ── Pass 2 — rookies ──────────────────────────────────────────────
        var (rookiesProjected, rookiesSkipped) = await ProjectRookiesAsync(
            request, spreadByTeam, projectedSleeperIds, cancellationToken);

        sw.Stop();

        logger.LogInformation(
            "Projections complete — {Calculated} from history, {Rookies} rookie priors, " +
            "{Skipped} skipped, {RookiesSkipped} rookies with no signal, in {Elapsed}ms " +
            "(basis {Basis}/{BasisSeason}). QB:{QB} RB:{RB} WR:{WR} TE:{TE}",
            calculated, rookiesProjected, skipped, rookiesSkipped, sw.ElapsedMilliseconds,
            basis, basisSeason,
            countByPosition["QB"], countByPosition["RB"],
            countByPosition["WR"], countByPosition["TE"]);

        return Result.Success(new CalculateProjectionsResult(
            calculated, skipped, request.Season, request.Week, sw.Elapsed,
            basis.ToString(), basisSeason, rookiesProjected, rookiesSkipped));
    }

    /// <summary>
    /// Builds priors for rookies, who by definition have no game logs for pass 1 to
    /// regress. Everything is batch-loaded — one query per source, no N+1.
    /// </summary>
    private async Task<(int Projected, int Skipped)> ProjectRookiesAsync(
        CalculateProjectionsCommand request,
        Dictionary<string, decimal> spreadByTeam,
        HashSet<string> alreadyProjected,
        CancellationToken ct)
    {
        var projected = 0;
        var skipped = 0;

        var rookies = await playerRepository.GetRookiesAsync(null, ct);

        var candidates = rookies
            .Where(p => SupportedPositions.Contains(p.Position.ToString().ToUpperInvariant()))
            .Where(p => !string.IsNullOrWhiteSpace(p.SleeperPlayerId))
            .Where(p => !alreadyProjected.Contains(p.SleeperPlayerId!))
            .ToList();

        if (candidates.Count == 0)
        {
            logger.LogInformation("No rookie candidates to project for {Season}", request.Season);
            return (0, 0);
        }

        var ids = candidates.Select(p => p.SleeperPlayerId!).ToList();

        var depthRows = await depthChartRepository.GetLatestBySleeperIdsAsync(
            ids, request.Season, ct);
        var depthBySleeper = depthRows
            .Where(d => !string.IsNullOrEmpty(d.SleeperPlayerId))
            .GroupBy(d => d.SleeperPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rankRows = await rookieRankingRepository.GetAllBySeasonAndTypeAsync(
            request.Season, "Rookie", ct);
        var ranksBySleeper = rankRows
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId))
            .GroupBy(r => r.SleeperPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var combineRows = await combineResultRepository.GetBySleeperPlayerIdsAsync(ids, ct);
        var combineBySleeper = combineRows
            .Where(c => !string.IsNullOrEmpty(c.SleeperPlayerId))
            .GroupBy(c => c.SleeperPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "Rookie prior inputs for {Season}: {Candidates} candidates, " +
            "{Depth} matched a depth chart, {Ranks} matched a consensus rank, {Combine} matched combine data",
            request.Season, candidates.Count,
            candidates.Count(p => depthBySleeper.ContainsKey(p.SleeperPlayerId!)),
            candidates.Count(p => ranksBySleeper.ContainsKey(p.SleeperPlayerId!)),
            candidates.Count(p => combineBySleeper.ContainsKey(p.SleeperPlayerId!)));

        foreach (var player in candidates)
        {
            try
            {
                var sleeperId = player.SleeperPlayerId!;
                var position = player.Position.ToString().ToUpperInvariant();

                depthBySleeper.TryGetValue(sleeperId, out var depth);
                ranksBySleeper.TryGetValue(sleeperId, out var rank);
                combineBySleeper.TryGetValue(sleeperId, out var combine);

                var result = RookieProjectionService.Project(new RookieProjectionInput
                {
                    PlayerId = player.GsisId ?? string.Empty,
                    SleeperPlayerId = sleeperId,
                    PlayerName = player.FullName,
                    Position = position,
                    NflTeam = player.NflTeam ?? depth?.NflTeam ?? "FA",
                    Season = request.Season,
                    DepthTeam = depth?.DepthTeam,
                    FantasyProsPositionRank = rank?.PositionRank,
                    AthleticismScore = combine?.AthleticismScore
                });

                if (result.IsSkipped)
                {
                    skipped++;
                    logger.LogDebug(
                        "Rookie {Player} ({Pos}) not projected — {Reason}",
                        player.FullName, position, result.SkipReason);
                    continue;
                }

                var team = player.NflTeam ?? depth?.NflTeam ?? "FA";
                var spread = spreadByTeam.TryGetValue(team, out var s) ? s : 0m;
                var correlation = GameScriptClassifier.Classify(spread);

                var doc = MapRookieToDocument(
                    player.GsisId, sleeperId, player.FullName, position, team,
                    result, request.Season, request.Week, correlation);

                await projectionRepository.UpsertAsync(doc, ct);
                projected++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to build rookie prior for {Player} ({SleeperId})",
                    player.FullName, player.SleeperPlayerId);
                skipped++;
            }
        }

        return (projected, skipped);
    }

    /// <summary>
    /// Current season if it has game logs; otherwise the prior season, explicitly
    /// flagged as carryover. Only looks back one season — a two-season-old projection
    /// is not a projection.
    /// </summary>
    private static (ProjectionBasis Basis, int BasisSeason) ResolveBasis(
        IReadOnlyDictionary<int, long> countsBySeason, int requestedSeason)
    {
        if (countsBySeason.TryGetValue(requestedSeason, out var current) && current > 0)
            return (ProjectionBasis.CurrentSeason, requestedSeason);

        var prior = requestedSeason - 1;
        if (countsBySeason.TryGetValue(prior, out var priorCount) && priorCount > 0)
            return (ProjectionBasis.PriorSeasonCarryover, prior);

        return (ProjectionBasis.None, 0);
    }

    private static PlayerProjectionDocument MapToDocument(
        StatLineProjectionResult projection,
        PlayerGameLogDocument recentLog,
        int season,
        int week,
        CorrelationMetadata correlation)
    {
        var statLine = projection.StatLine;
        var position = recentLog.Position;

        var (standard, halfPpr, fullPpr) = ScoreAllFormats(statLine, position);

        return new PlayerProjectionDocument
        {
            PlayerId = projection.PlayerId,
            SleeperPlayerId = recentLog.SleeperPlayerId ?? string.Empty,
            PlayerName = recentLog.PlayerName,
            Position = position,
            NflTeam = recentLog.NflTeam,
            OpponentTeam = recentLog.OpponentTeam ?? "UNK",
            Season = season,
            Week = week,

            StatLine = statLine,
            Basis = projection.Basis.ToString(),
            BasisSeason = projection.BasisSeason,

            ProjectedPoints = standard,
            ProjectedPointsPpr = fullPpr,
            ProjectedPointsHalfPpr = halfPpr,

            // Retained for legacy readers. The stat-line model has no single
            // "weighted average points" term, so this mirrors the half-PPR value.
            WeightedAvgPoints = halfPpr,
            MatchupAdjustmentFactor = projection.MatchupAdjustmentFactor,
            SnapPctInput = projection.SnapPctInput,
            TargetShareInput = projection.TargetShareInput,
            UsageTrendMultiplier = projection.UsageTrendMultiplier,
            AvailabilityRate = projection.AvailabilityRate,
            GameSampleSize = projection.GameSampleSize,

            // Not produced by the stat-line model — the old value was the R² of a
            // points-on-week-index trend line, which no longer exists.
            RSquared = 0m,

            // Deprecated by Epic 20 — a stat-line projection has no single format.
            // Left as "HalfPpr" so any existing reader that filters on this value
            // keeps behaving; remove once nothing reads it.
            ScoringFormat = "HalfPpr",
            GameScript = correlation.Script.ToString(),
            RbVolumeMultiplier = correlation.RbVolumeMultiplier,
            WrTeVolumeMultiplier = correlation.WrTeVolumeMultiplier,
            SpreadInput = correlation.Spread,
            CalculatedAt = DateTime.UtcNow
        };
    }

    private static PlayerProjectionDocument MapRookieToDocument(
        string? gsisId,
        string sleeperPlayerId,
        string playerName,
        string position,
        string nflTeam,
        RookieProjectionResult result,
        int season,
        int week,
        CorrelationMetadata correlation)
    {
        var statLine = result.StatLine;
        var (standard, halfPpr, fullPpr) = ScoreAllFormats(statLine, position);

        // The projection upsert key is PlayerId + Season + Week. Most rookies have
        // no GSIS id yet, and an empty PlayerId would collapse every one of them
        // onto a single document. Fall back to a namespaced Sleeper id so each
        // rookie keeps his own row and never collides with a real GSIS id.
        var playerId = string.IsNullOrWhiteSpace(gsisId)
            ? $"sleeper:{sleeperPlayerId}"
            : gsisId;

        return new PlayerProjectionDocument
        {
            PlayerId = playerId,
            SleeperPlayerId = sleeperPlayerId,
            PlayerName = playerName,
            Position = position,
            NflTeam = nflTeam,
            OpponentTeam = "UNK",
            Season = season,
            Week = week,

            StatLine = statLine,
            Basis = ProjectionBasis.RookieProjection.ToString(),
            BasisSeason = season,

            ProjectedPoints = standard,
            ProjectedPointsPpr = fullPpr,
            ProjectedPointsHalfPpr = halfPpr,

            WeightedAvgPoints = halfPpr,
            MatchupAdjustmentFactor = 1m,
            SnapPctInput = 0m,
            TargetShareInput = 0m,
            UsageTrendMultiplier = 1m,
            AvailabilityRate = statLine.AvailabilityRate,

            // No games behind this number — that is the point of the basis flag.
            GameSampleSize = 0,
            RSquared = 0m,

            ScoringFormat = "HalfPpr",
            GameScript = correlation.Script.ToString(),
            RbVolumeMultiplier = correlation.RbVolumeMultiplier,
            WrTeVolumeMultiplier = correlation.WrTeVolumeMultiplier,
            SpreadInput = correlation.Spread,
            CalculatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// L1 — score the SAME stat line in each cached format. Any consumer that needs
    /// a league's real settings should call FantasyScoringService itself rather than
    /// reading one of these columns.
    /// </summary>
    private static (decimal Standard, decimal HalfPpr, decimal FullPpr) ScoreAllFormats(
        ProjectedStatLine statLine, string position)
        => (FantasyScoringService.Score(statLine, LeagueScoringSettings.Standard, position),
            FantasyScoringService.Score(statLine, LeagueScoringSettings.HalfPpr, position),
            FantasyScoringService.Score(statLine, LeagueScoringSettings.FullPpr, position));
}
