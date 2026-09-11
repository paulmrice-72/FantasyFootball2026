// FF.Application/Features/Projections/Commands/CalculateProjections/CalculateProjectionsCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.Services;
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
    INflScheduleRepository scheduleRepository,
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

        // Keys normalised: vegas_lines is written through TeamNameResolver, which
        // produces the nflverse LA, while every team string this loop compares
        // against comes from Sleeper or a game log. FAN-178.
        var spreadByTeam = vegasLines
            .SelectMany(v => new[]
            {
                (Team: NflTeamNormalizer.Normalize(v.HomeTeam), Spread: v.HomeSpread),
                (Team: NflTeamNormalizer.Normalize(v.AwayTeam), Spread: v.AwaySpread)
            })
            .Where(x => x.Team.Length > 0)
            .GroupBy(x => x.Team, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Spread, StringComparer.OrdinalIgnoreCase);

        // ── Schedule (FAN-178) ────────────────────────────────────────────
        // Who each team actually plays this week, and the closing line if one is
        // posted. Before this existed the opponent came off the player's most
        // recent game log, which in a carryover run is his LAST GAME OF THE
        // PRIOR SEASON — so Week 1 was conditioned on a January opponent.
        var schedule = await scheduleRepository.GetByWeekAsync(
            request.Season, request.Week, cancellationToken);

        // team → (opponent, isHome). A team absent from this map is on bye.
        var gameByTeam = new Dictionary<string, (string Opponent, bool IsHome)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var game in schedule)
        {
            if (game.HomeTeam.Length > 0 && game.AwayTeam.Length > 0)
            {
                gameByTeam[game.HomeTeam] = (game.AwayTeam, true);
                gameByTeam[game.AwayTeam] = (game.HomeTeam, false);
            }

            // Fall back to the schedule's closing line only where The Odds API
            // has nothing. Live odds are closer to kickoff and win where both
            // exist; the schedule covers roughly seven weeks out and is the only
            // source at all in dev, where the Hangfire odds job never runs.
            if (!game.SpreadLine.HasValue) continue;

            if (game.HomeTeam.Length > 0 && !spreadByTeam.ContainsKey(game.HomeTeam))
                spreadByTeam[game.HomeTeam] = game.SpreadLine.Value;

            if (game.AwayTeam.Length > 0 && !spreadByTeam.ContainsKey(game.AwayTeam))
                spreadByTeam[game.AwayTeam] = -game.SpreadLine.Value;
        }

        if (gameByTeam.Count == 0)
        {
            // Loud, because the failure is invisible otherwise: with no schedule
            // every player resolves to "no game", the matchup lookup degrades to
            // a neutral 50 for the whole board, and the run still reports success.
            logger.LogWarning(
                "No schedule rows for Season {Season} Week {Week} — every projection in this " +
                "run will be matchup-neutral and carry no opponent. Run " +
                "POST /api/v1/admin/sync-nfl-schedule first.",
                request.Season, request.Week);
        }
        else
        {
            logger.LogInformation(
                "Schedule loaded for Season {Season} Week {Week}: {Teams} teams playing, " +
                "{Spreads} with a spread ({Vegas} from vegas_lines).",
                request.Season, request.Week, gameByTeam.Count,
                spreadByTeam.Count, vegasLines.Count * 2);
        }

        // Current team per player, from Sleeper — the roster system of record.
        // The game log's NflTeam is last season's, so a player who changed teams
        // would otherwise be scheduled against his OLD team's opponent.
        var allPlayers = await playerRepository.GetAllAsync(cancellationToken);

        var currentTeamBySleeperId = allPlayers
            .Where(p => !string.IsNullOrEmpty(p.SleeperPlayerId)
                     && !string.IsNullOrEmpty(p.NflTeam))
            .GroupBy(p => p.SleeperPlayerId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => NflTeamNormalizer.Normalize(g.First().NflTeam),
                StringComparer.OrdinalIgnoreCase);

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

        // ── Depth-chart role gate (2026-09-07, FAN-137) ───────────────────
        // Loaded for the REQUESTED season, not the basis season: the question
        // this answers is "does he have the job now", and last season's chart
        // answers the wrong question. QB only — see DepthRoleAdjustment for why
        // this deliberately does not generalize to RB/WR/TE.
        var qbDepthRows = await depthChartRepository.GetLatestByPositionAsync(
            "QB", request.Season, cancellationToken);

        var depthTeamBySleeperId = qbDepthRows
            .GroupBy(d => d.SleeperPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DepthTeam, StringComparer.OrdinalIgnoreCase);

        if (depthTeamBySleeperId.Count == 0)
        {
            // Loud, because the failure is invisible otherwise: no depth data
            // means every backup QB projects as though he were the starter, and
            // the run still reports full success.
            logger.LogWarning(
                "No QB depth chart rows for season {Season} — the role gate will not " +
                "fire and backup quarterbacks will project on their prior-season " +
                "starter usage. Run the depth chart sync before trusting QB rankings.",
                request.Season);
        }
        else
        {
            logger.LogInformation(
                "QB role gate armed: {Total} depth rows, {Starters} at depth 1.",
                depthTeamBySleeperId.Count,
                depthTeamBySleeperId.Count(kv => kv.Value == 1));
        }

        var roleGated = 0;

        // Players whose current team has no game this week — a real bye, or a
        // schedule that was never imported. Counted rather than inferred: the
        // two look identical per player and very different in aggregate.
        var byeOrNoGame = 0;

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

                    // The team he plays for NOW. The game log's NflTeam is the
                    // basis season's, so for anyone who moved in the offseason it
                    // names the wrong franchise — and would schedule him against
                    // his old team's opponent.
                    var currentTeam = !string.IsNullOrEmpty(recentLog.SleeperPlayerId)
                        && currentTeamBySleeperId.TryGetValue(
                               recentLog.SleeperPlayerId, out var rosteredTeam)
                        ? rosteredTeam
                        : NflTeamNormalizer.Normalize(recentLog.NflTeam);

                    // Opponent comes from the schedule for the week being
                    // projected. Absent = bye week (or an un-imported schedule),
                    // which resolves to no matchup and therefore a neutral 50 —
                    // the same treatment as a missing defensive ranking.
                    var hasGame = gameByTeam.TryGetValue(currentTeam, out var game);
                    var opponentTeam = hasGame ? game.Opponent : string.Empty;

                    if (!hasGame) byeOrNoGame++;

                    // No line posted (or preseason) → spread 0 → neutral Competitive script.
                    var spread = spreadByTeam.TryGetValue(currentTeam, out var s) ? s : 0m;
                    var correlation = GameScriptClassifier.Classify(spread);

                    var input = await inputBuilder.BuildStatLineInputAsync(
                        playerId,
                        position,
                        opponentTeam.Length > 0 ? opponentTeam : "UNK",
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

                    int? depthTeam = null;
                    if (!string.IsNullOrEmpty(recentLog.SleeperPlayerId)
                        && depthTeamBySleeperId.TryGetValue(recentLog.SleeperPlayerId, out var dt))
                    {
                        depthTeam = dt;
                    }

                    var doc = MapToDocument(
                        projection, recentLog, request.Season, request.Week, correlation, depthTeam,
                        currentTeam, opponentTeam, hasGame && game.IsHome);

                    if (doc.RoleMultiplier < 1m)
                    {
                        roleGated++;
                        logger.LogInformation(
                            "Role gate: {Name} ({Pos}, {Team}) depth {Depth} → ×{Mult} [{Role}], " +
                            "{Ppr} full-PPR pts/gm after adjustment.",
                            doc.PlayerName, position, doc.NflTeam, depthTeam,
                            doc.RoleMultiplier, doc.DepthRole, doc.ProjectedPointsPpr);
                    }

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
            request, spreadByTeam, gameByTeam, projectedSleeperIds, cancellationToken);

        sw.Stop();

        logger.LogInformation(
            "Projections complete — {Calculated} from history, {Rookies} rookie priors, " +
            "{Skipped} skipped, {RookiesSkipped} rookies with no signal, " +
            "{RoleGated} suppressed by the depth role gate, {ByeOrNoGame} with no game " +
            "this week, in {Elapsed}ms " +
            "(basis {Basis}/{BasisSeason}). QB:{QB} RB:{RB} WR:{WR} TE:{TE}",
            calculated, rookiesProjected, skipped, rookiesSkipped, roleGated, byeOrNoGame,
            sw.ElapsedMilliseconds, basis, basisSeason,
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
        Dictionary<string, (string Opponent, bool IsHome)> gameByTeam,
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

                var team = NflTeamNormalizer.Normalize(
                    player.NflTeam ?? depth?.NflTeam ?? "FA");

                // Same schedule resolution as pass 1 — rookies were stamping a
                // literal "UNK" opponent, so every rookie on the board rendered
                // with no game regardless of what the schedule said.
                var rookieHasGame = gameByTeam.TryGetValue(team, out var rookieGame);
                var rookieOpponent = rookieHasGame ? rookieGame.Opponent : string.Empty;

                var spread = spreadByTeam.TryGetValue(team, out var s) ? s : 0m;
                var correlation = GameScriptClassifier.Classify(spread);

                // RookieProjectionService already consumes DepthTeam as a model
                // input, so the rookie line is depth-aware before it gets here —
                // no second multiplier, just stamp what it used so the two paths
                // store the same explanatory fields.
                var doc = MapRookieToDocument(
                    player.GsisId, sleeperId, player.FullName, position, team,
                    result, request.Season, request.Week, correlation, depth?.DepthTeam,
                    rookieOpponent, rookieHasGame && rookieGame.IsHome);

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
        CorrelationMetadata correlation,
        int? depthTeam,
        string nflTeam,
        string opponentTeam,
        bool isHomeGame)
    {
        var position = recentLog.Position;

        // Role gate applies to the STAT LINE, before scoring — the stat line is
        // canonical under Epic 20, so adjusting the cached point columns instead
        // would leave the stored line disagreeing with its own points and every
        // format-aware reader would recompute its way back to the un-gated
        // number.
        var (roleMultiplier, depthRole) = DepthRoleAdjustment.Resolve(position, depthTeam);

        var statLine = roleMultiplier < 1m
            ? projection.StatLine.Scale(roleMultiplier)
            : projection.StatLine;

        var (standard, halfPpr, fullPpr) = ScoreAllFormats(statLine, position);

        return new PlayerProjectionDocument
        {
            PlayerId = projection.PlayerId,
            SleeperPlayerId = recentLog.SleeperPlayerId ?? string.Empty,
            PlayerName = recentLog.PlayerName,
            Position = position,
            // Both resolved by the caller from Sleeper and the schedule — NOT
            // from recentLog, whose NflTeam and OpponentTeam are both basis-season
            // values. That was FAN-178: a Week 1 projection carrying the player's
            // old team and his final opponent of the previous January.
            NflTeam = nflTeam,
            OpponentTeam = opponentTeam,
            IsHomeGame = isHomeGame,
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

            DepthTeam = depthTeam,
            RoleMultiplier = roleMultiplier,
            DepthRole = depthRole,

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
        CorrelationMetadata correlation,
        int? depthTeam,
        string opponentTeam,
        bool isHomeGame)
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
            OpponentTeam = opponentTeam,
            IsHomeGame = isHomeGame,
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

            // Rookie priors are built FROM depth, not gated after it — the
            // multiplier stays 1.0 and the role reads RookieDepthPrior so a
            // query can tell the two mechanisms apart.
            DepthTeam = depthTeam,
            RoleMultiplier = 1m,
            DepthRole = "RookieDepthPrior",

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
