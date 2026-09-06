// FF.Application/Features/Simulations/Commands/SeedSeasonAverageSims/SeedSeasonAverageSimsCommandHandler.cs
using System.Globalization;
using System.Net;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Simulations.Commands.SeedSeasonAverageSims;

public class SeedSeasonAverageSimsCommandHandler(
    ISimulationResultRepository simRepository,
    IPlayerRepository playerRepository,
    IPlayerIdResolutionService resolutionService,
    IHttpClientFactory httpClientFactory,
    ILogger<SeedSeasonAverageSimsCommandHandler> logger)
    : IRequestHandler<SeedSeasonAverageSimsCommand, SeedSeasonAverageSimsResult>
{
    private static readonly HashSet<string> SkillPositions = ["QB", "RB", "WR", "TE"];

    /// <summary>
    /// Sleeper's player table carries placeholder rows for retired/void entries.
    /// They all normalise to the same handful of keys and would otherwise sit in
    /// the name index competing with real players.
    /// </summary>
    private static readonly HashSet<string> PlaceholderNames =
        ["player invalid", "duplicate player", "invalid player", "unknown player"];

    // nflverse has renamed these columns more than once. Read every spelling we
    // have seen rather than assuming the current one — a rename silently blanks
    // the field, which is how the 2025 prod seed ended up with empty NflTeam.
    private static readonly string[] GsisIdColumns =
        ["player_id", "gsis_id", "player_gsis_id"];

    private static readonly string[] TeamColumns =
        ["recent_team", "team", "team_abbr", "recent_team_abbr"];

    private const string SeasonAggregateUrlTemplate = "https://github.com/nflverse/nflverse-data/releases/download/stats_player/stats_player_reg_{0}.csv";
    private const string WeeklyUrlTemplate = "https://github.com/nflverse/nflverse-data/releases/download/stats_player/stats_player_week_{0}.csv";

    public async Task<SeedSeasonAverageSimsResult> Handle(
        SeedSeasonAverageSimsCommand request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("SeedSeasonAverageSims starting for season {Season}", request.Season);

        string csv;
        bool isWeeklyFile;

        // ── 1: Get CSV content ────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(request.CsvContent))
        {
            // Provided directly — detect weekly vs aggregate by header
            csv = request.CsvContent;
            isWeeklyFile = LooksWeekly(csv);
            logger.LogInformation(
                "Using provided CSV content — weeklyFile={IsWeekly}, season {Season}",
                isWeeklyFile, request.Season);
        }
        else
        {
            var download = await DownloadAsync(request.Season, cancellationToken);
            csv = download.Content;
            isWeeklyFile = download.IsWeekly;
        }

        // ── 2: Parse CSV ──────────────────────────────────────────────────────
        var rows = ParseCsv(csv);
        logger.LogInformation(
            "Parsed {Count} rows from CSV (weeklyFile={IsWeekly})", rows.Count, isWeeklyFile);

        // ── 3: Filter / aggregate ─────────────────────────────────────────────
        List<Dictionary<string, string>> eligible;

        if (isWeeklyFile)
        {
            eligible = AggregateWeeklyToSeason(rows);
            logger.LogInformation(
                "{Count} players after aggregating weekly rows for {Season}",
                eligible.Count, request.Season);
        }
        else
        {
            eligible = rows
                .Where(r =>
                    r.TryGetValue("season_type", out var st) && st.Trim() == "REG"
                    && r.TryGetValue("position", out var pos)
                    && SkillPositions.Contains(pos.Trim().ToUpper())
                    && TryParseGames(r.GetValueOrDefault("games"), out var g) && g > 0)
                .ToList();
            logger.LogInformation("{Count} eligible REG-season rows after filtering", eligible.Count);
        }

        // ── 4: Build the identity indexes ─────────────────────────────────────
        //
        // Three ways to reach a Sleeper id, in descending order of trust:
        //
        //   1. nflverse gsis -> sleeper bridge (stable across seasons)
        //   2. the GsisId already stored on our own Player row
        //   3. normalised name + POSITION
        //
        // (3) used to be `GroupBy(name).ToDictionary(g => g.First())` — no position
        // check and no tiebreak, so with two "Kenneth Walker" rows in the player
        // table (8151 RB, 4634 WR) the winner was decided by whatever order
        // GetAllAsync happened to return. Verified 2026-09-02: dev wrote the RB's
        // 2024 season average onto the WR's id while prod wrote the same row onto
        // the RB's. Same code, same input, different answer per environment.
        var gsisToSleeper = NormalizeGsisMap(
            await resolutionService.BuildGsisToSleeperMapAsync(cancellationToken));

        var allPlayers = await playerRepository.GetAllAsync(cancellationToken);

        var indexablePlayers = allPlayers
            .Where(p => !string.IsNullOrWhiteSpace(p.SleeperPlayerId)
                        && !string.IsNullOrWhiteSpace(p.FullName)
                        && !PlaceholderNames.Contains(NormalizeName(p.FullName!)))
            .ToList();

        var playerBySleeperId = indexablePlayers
            .GroupBy(p => p.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.First());

        var playerByOwnGsis = indexablePlayers
            .Where(p => !string.IsNullOrWhiteSpace(p.GsisId))
            .GroupBy(p => p.GsisId!.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        // Keyed on name AND position — the whole group is kept so ambiguity is
        // visible at lookup time instead of being silently resolved by First().
        var playersByNameAndPosition = indexablePlayers
            .GroupBy(p => $"{NormalizeName(p.FullName!)}|{p.Position.ToString().ToUpperInvariant()}")
            .ToDictionary(g => g.Key, g => g.ToList());

        logger.LogInformation(
            "Identity indexes built — {Bridge} gsis bridge entries, {Players} indexable players, " +
            "{OwnGsis} with a stored GsisId",
            gsisToSleeper.Count, indexablePlayers.Count, playerByOwnGsis.Count);

        // ── 4b: Positional priors for small-sample shrinkage ──────────────────
        //
        // 2026-09-07. The per-game average below was `total / games` with `games > 0`
        // as the only gate, so one good afternoon became a season-long rate that
        // outranked players with seventeen games of evidence. Measured case: Joe
        // Milton's 2024 row seeded at 19.24 half-PPR per game off a two-game
        // sample — a top-five quarterback rate, stored permanently, and the origin
        // of the "why is Joe Milton on my dynasty board" complaint.
        //
        // Shrinkage rather than a cutoff. A hard `games >= N` filter would erase
        // Joe Burrow's eight-game 2025 along with the noise, and a player who
        // genuinely missed half a season still has a real average — it is just
        // less certain. Weight a player's own rate by how much of a sample he
        // brought and give the rest to what an unremarkable player at his position
        // averages.
        //
        // The prior is the MEDIAN OVER EVERY eligible row at the position, backups
        // included — deliberately not the median among qualified starters. The
        // question shrinkage answers is "what should we expect from a player we
        // know little about", and the answer to that is not "a starter".
        var positionalPriors = BuildPositionalPriors(eligible);

        foreach (var (pos, prior) in positionalPriors.OrderBy(p => p.Key))
        {
            logger.LogInformation(
                "Shrinkage prior for {Position}: {Prior:F2} half-PPR pts/gm " +
                "(median of all eligible {Season} rows at the position)",
                pos, prior, request.Season);
        }

        // ── 5: Build sim docs ─────────────────────────────────────────────────
        var toUpsert = new List<SimulationResultDocument>();
        int skipped = 0, unmatched = 0, matchedByGsis = 0, matchedByName = 0, ambiguousSkipped = 0;
        int shrunk = 0;

        foreach (var row in eligible)
        {
            var playerName = row.GetValueOrDefault("player_display_name")
                ?? row.GetValueOrDefault("player_name")
                ?? string.Empty;
            var position = row["position"].Trim().ToUpper();
            var nflTeam = FirstNonEmpty(row, TeamColumns) ?? string.Empty;
            var gsisId = FirstNonEmpty(row, GsisIdColumns)?.Trim();

            if (!TryParseGames(row.GetValueOrDefault("games"), out var games) || games <= 0)
            {
                skipped++;
                continue;
            }

            if (!decimal.TryParse(row.GetValueOrDefault("fantasy_points"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var stdPts))
            {
                skipped++;
                continue;
            }

            if (!decimal.TryParse(row.GetValueOrDefault("receptions"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var receptions))
                receptions = 0m;

            var halfPprTotal = stdPts + (receptions * 0.5m);
            var rawAvgHalfPpr = halfPprTotal / games;

            var prior = positionalPriors.TryGetValue(position, out var p) ? p : rawAvgHalfPpr;
            var avgHalfPpr = ApplyShrinkage(rawAvgHalfPpr, games, prior);

            if (avgHalfPpr != rawAvgHalfPpr)
            {
                shrunk++;
                logger.LogDebug(
                    "Shrunk {Name} ({Pos}): {Raw:F2} → {Adj:F2} on {Games} game(s), prior {Prior:F2}",
                    playerName, position, rawAvgHalfPpr, avgHalfPpr, games, prior);
            }

            FF.Domain.Entities.Player? player = null;

            // (1) gsis -> sleeper bridge
            if (!string.IsNullOrWhiteSpace(gsisId)
                && gsisToSleeper.TryGetValue(gsisId, out var sleeperId)
                && playerBySleeperId.TryGetValue(sleeperId, out player))
            {
                matchedByGsis++;
            }
            // (2) GsisId stored on our own Player row
            else if (!string.IsNullOrWhiteSpace(gsisId)
                     && playerByOwnGsis.TryGetValue(gsisId, out player))
            {
                matchedByGsis++;
            }
            // (3) name + position, with an explicit tiebreak and an explicit refusal
            else
            {
                var key = $"{NormalizeName(playerName)}|{position}";

                if (!playersByNameAndPosition.TryGetValue(key, out var candidates))
                {
                    logger.LogWarning(
                        "No Sleeper match for '{Name}' ({Pos}) — gsis={Gsis}. " +
                        "This player will have NO season average for {Season}.",
                        playerName, position, gsisId ?? "-", request.Season);
                    unmatched++;
                    continue;
                }

                player = ResolveAmbiguity(candidates, gsisId, nflTeam);

                if (player is null)
                {
                    // Refusing is deliberate. A wrong bind writes one player's
                    // production onto another player's id, and nothing downstream
                    // can tell that apart from real data.
                    logger.LogWarning(
                        "Ambiguous name match for '{Name}' ({Pos}) — {Count} candidates " +
                        "[{Candidates}]. Skipping rather than guessing.",
                        playerName, position, candidates.Count,
                        string.Join(", ", candidates.Select(c => $"{c.SleeperPlayerId}:{c.NflTeam ?? "-"}")));
                    ambiguousSkipped++;
                    continue;
                }

                matchedByName++;
            }

            toUpsert.Add(new SimulationResultDocument
            {
                SleeperPlayerId = player.SleeperPlayerId!,
                PlayerName = player.FullName ?? playerName,
                Position = position,
                NflTeam = nflTeam,
                Season = request.Season,
                Week = 0,
                Median = Math.Round(avgHalfPpr, 2),
                Floor = Math.Round(avgHalfPpr * 0.6m, 2),
                Ceiling = Math.Round(avgHalfPpr * 1.5m, 2),
                Mean = Math.Round(avgHalfPpr, 2),
                BaseProjection = Math.Round(avgHalfPpr, 2),
                StandardDeviation = Math.Round(avgHalfPpr * 0.20m, 2),
                BoomProbability = 0.20m,
                BustProbability = 0.15m,
                Iterations = 0,
                ScoringFormat = "HalfPPR",
                CalculatedAt = DateTime.UtcNow,
                OpponentTeam = string.Empty,
                Spread = 0,
                GameScript = "Neutral",
                PlayerRole = "SeasonAverage",
                GameSampleSize = games
            });
        }

        logger.LogInformation(
            "Upserting {Count} season-average sim docs for season {Season} " +
            "(byGsis={Gsis}, byName={Name}, skipped={Skip}, unmatched={Unmatched}, " +
            "ambiguous={Ambiguous}, shrunk={Shrunk})",
            toUpsert.Count, request.Season, matchedByGsis, matchedByName,
            skipped, unmatched, ambiguousSkipped, shrunk);

        if (matchedByGsis == 0 && matchedByName > 0)
        {
            logger.LogWarning(
                "Every player in the {Season} seed was resolved by NAME — the gsis bridge " +
                "matched nothing. Check that the nflverse file still carries one of [{Columns}] " +
                "and that the roster CSVs still carry sleeper_id.",
                request.Season, string.Join(", ", GsisIdColumns));
        }

        await simRepository.UpsertBatchAsync(toUpsert, cancellationToken);

        return new SeedSeasonAverageSimsResult(
            Seeded: toUpsert.Count,
            Skipped: skipped,
            Unmatched: unmatched,
            MatchedByGsis: matchedByGsis,
            MatchedByName: matchedByName,
            AmbiguousSkipped: ambiguousSkipped);
    }

    // ── Download ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Tries the season aggregate, then the weekly file. Throws
    /// <see cref="NflverseDataUnavailableException"/> with NotPublished set only
    /// when nflverse actually answered 404 for both.
    /// </summary>
    private async Task<(string Content, bool IsWeekly)> DownloadAsync(
        int season, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("NflverseClient");

        var aggregate = await TryDownloadAsync(
            http, string.Format(SeasonAggregateUrlTemplate, season), cancellationToken);

        if (aggregate.Content is not null && !LooksWeekly(aggregate.Content))
        {
            logger.LogInformation("Using season aggregate file for {Season}", season);
            return (aggregate.Content, false);
        }

        logger.LogInformation(
            "Season aggregate unusable for {Season} (notFound={NotFound}, error={Error}) — trying weekly file",
            season, aggregate.NotFound, aggregate.Error?.Message ?? "-");

        var weekly = await TryDownloadAsync(
            http, string.Format(WeeklyUrlTemplate, season), cancellationToken);

        if (weekly.Content is not null)
            return (weekly.Content, true);

        // Both failed. Only call it "not published" when nflverse said so.
        var bothNotFound = aggregate.NotFound && weekly.NotFound;

        var message = bothNotFound
            ? $"nflverse has not published stats for season {season} yet " +
              $"(both stats_player_reg_{season}.csv and stats_player_week_{season}.csv return 404). " +
              $"This is normal before the season is played — upload the CSV via the Admin import panel " +
              $"if you have it from another source."
            : $"Could not retrieve nflverse stats for season {season}: " +
              $"{weekly.Error?.GetType().Name ?? "unknown error"} — {weekly.Error?.Message ?? "no detail"}. " +
              $"nflverse did NOT report the file as missing, so this is a connectivity or timeout problem " +
              $"in this environment, not a missing-season problem. Check egress to github.com and the " +
              $"NflverseClient timeout.";

        throw new NflverseDataUnavailableException(
            season, bothNotFound, message, weekly.Error ?? aggregate.Error);
    }

    private async Task<(string? Content, bool NotFound, Exception? Error)> TryDownloadAsync(
        HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, true, null);

            if (!response.IsSuccessStatusCode)
                return (null, false,
                    new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} for {url}"));

            return (await response.Content.ReadAsStringAsync(cancellationToken), false, null);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient surfaces its own timeout as TaskCanceledException. Without
            // this guard it reads as "the caller cancelled", which it is not.
            return (null, false, new TimeoutException($"Request to {url} timed out.", ex));
        }
        catch (Exception ex)
        {
            return (null, false, ex);
        }
    }

    // ── Identity helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic tiebreak for a name+position group. Returns null when the
    /// group cannot be resolved — the caller must then skip, not guess.
    /// </summary>
    private static FF.Domain.Entities.Player? ResolveAmbiguity(
        List<FF.Domain.Entities.Player> candidates, string? gsisId, string nflTeam)
    {
        if (candidates.Count == 1) return candidates[0];

        // The row's own gsis, if our Player row happens to carry it.
        if (!string.IsNullOrWhiteSpace(gsisId))
        {
            var byGsis = candidates
                .Where(c => string.Equals(c.GsisId?.Trim(), gsisId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byGsis.Count == 1) return byGsis[0];
        }

        // Same NFL team as the stat row.
        if (!string.IsNullOrWhiteSpace(nflTeam))
        {
            var byTeam = candidates
                .Where(c => string.Equals(c.NflTeam, nflTeam, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byTeam.Count == 1) return byTeam[0];
        }

        // Currently on a roster at all beats a bare historical shell.
        var rostered = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.NflTeam))
            .ToList();
        if (rostered.Count == 1) return rostered[0];

        return null;
    }

    /// <summary>
    /// nflverse roster CSVs are written from R, which serialises numeric ids with a
    /// trailing ".0" ("4881.0"). Our SleeperPlayerId is "4881", so an unnormalised
    /// bridge value misses every lookup and quietly demotes the whole import to
    /// name matching.
    /// </summary>
    private static Dictionary<string, string> NormalizeGsisMap(Dictionary<string, string> raw)
    {
        var map = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in raw)
        {
            var gsis = kvp.Key?.Trim();
            var sleeper = NormalizeSleeperId(kvp.Value);

            if (string.IsNullOrWhiteSpace(gsis) || string.IsNullOrWhiteSpace(sleeper))
                continue;

            map.TryAdd(gsis, sleeper);
        }

        return map;
    }

    private static string NormalizeSleeperId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var id = raw.Trim();

        // "4881.0" -> "4881"; leave genuinely non-numeric ids (team defences) alone.
        var dot = id.IndexOf('.');
        if (dot > 0 && id[(dot + 1)..].All(c => c == '0'))
            id = id[..dot];

        return id;
    }

    private static string? FirstNonEmpty(Dictionary<string, string> row, string[] columns)
    {
        foreach (var c in columns)
        {
            var v = row.GetValueOrDefault(c);
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return null;
    }

    /// <summary>
    /// Games below which a player's own rate stops being taken at face value.
    /// Half a season — enough that a hot two-game stretch cannot carry the number,
    /// low enough that a genuinely injured starter keeps most of his own signal.
    /// </summary>
    private const decimal FullWeightGames = 8m;

    /// <summary>
    /// Blends a player's own per-game rate toward a positional prior in proportion
    /// to how much sample he brought. At or above <see cref="FullWeightGames"/> the
    /// rate is returned untouched, so this changes nothing for the great majority
    /// of rows.
    ///
    /// Worked example — Joe Milton, 2024, 2 games, 19.24 raw against a QB prior
    /// near 9: weight 0.25, giving 0.25 × 19.24 + 0.75 × 9 ≈ 11.6. Still a real
    /// number rather than a zero, because he did play well; no longer a top-five
    /// quarterback rate built on one afternoon.
    /// </summary>
    private static decimal ApplyShrinkage(decimal rawAverage, int games, decimal positionalPrior)
    {
        if (games >= FullWeightGames) return rawAverage;

        var weight = games / FullWeightGames;
        return Math.Round((weight * rawAverage) + ((1m - weight) * positionalPrior), 2);
    }

    /// <summary>
    /// Median per-game half-PPR by position across every eligible row. Median, not
    /// mean, because the very rows this exists to correct — a handful of extreme
    /// small-sample rates — would drag a mean upward and weaken the prior exactly
    /// where it is needed most.
    /// </summary>
    private static Dictionary<string, decimal> BuildPositionalPriors(
        IReadOnlyList<Dictionary<string, string>> eligible)
    {
        var byPosition = new Dictionary<string, List<decimal>>();

        foreach (var row in eligible)
        {
            if (!row.TryGetValue("position", out var rawPos)) continue;
            var position = rawPos.Trim().ToUpper();

            if (!TryParseGames(row.GetValueOrDefault("games"), out var games) || games <= 0)
                continue;

            if (!decimal.TryParse(row.GetValueOrDefault("fantasy_points"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var stdPts))
                continue;

            if (!decimal.TryParse(row.GetValueOrDefault("receptions"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var receptions))
                receptions = 0m;

            var avg = (stdPts + (receptions * 0.5m)) / games;

            if (!byPosition.TryGetValue(position, out var list))
                byPosition[position] = list = [];

            list.Add(avg);
        }

        return byPosition.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var sorted = kv.Value.OrderBy(v => v).ToList();
                var mid = sorted.Count / 2;
                return sorted.Count % 2 == 1
                    ? sorted[mid]
                    : Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 4);
            });
    }

    /// <summary>
    /// "games" arrives as "17" from some files and "17.0" from others. The old
    /// code filtered with decimal.TryParse and then read with int.TryParse, so a
    /// float-formatted column passed the filter and was skipped in the body.
    /// </summary>
    private static bool TryParseGames(string? raw, out int games)
    {
        games = 0;
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return false;
        games = (int)Math.Round(d, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool LooksWeekly(string csv)
    {
        var firstLine = csv.Split('\n').FirstOrDefault() ?? string.Empty;
        return firstLine.Contains(",week,", StringComparison.OrdinalIgnoreCase)
            || firstLine.StartsWith("week,", StringComparison.OrdinalIgnoreCase)
            || firstLine.Contains("\"week\"", StringComparison.OrdinalIgnoreCase);
    }

    // ── Aggregation ───────────────────────────────────────────────────────────

    private static List<Dictionary<string, string>> AggregateWeeklyToSeason(
        List<Dictionary<string, string>> weeklyRows)
    {
        var eligible = weeklyRows
            .Where(r =>
                r.TryGetValue("season_type", out var st) && st.Trim() == "REG"
                && r.TryGetValue("position", out var pos)
                && SkillPositions.Contains(pos.Trim().ToUpper())
                && r.TryGetValue("fantasy_points", out var fp)
                && decimal.TryParse(fp, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _))
            .ToList();

        return eligible
            .GroupBy(r =>
            {
                // Group on the stable gsis id when the file carries it. Grouping on
                // the display name splits a player whose name changes mid-file —
                // nflverse alternates "Kenneth Walker" and "Kenneth Walker III"
                // between seasons, so this is not hypothetical.
                var gsis = FirstNonEmpty(r, GsisIdColumns);
                if (!string.IsNullOrWhiteSpace(gsis)) return $"gsis:{gsis}";

                var name = r.GetValueOrDefault("player_display_name")
                    ?? r.GetValueOrDefault("player_name")
                    ?? string.Empty;
                var pos = r["position"].Trim().ToUpper();
                return $"name:{name}|{pos}";
            })
            .Select(g =>
            {
                var sample = g.First();

                var totalPts = g.Sum(r =>
                    decimal.TryParse(r.GetValueOrDefault("fantasy_points"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m);

                var totalRec = g.Sum(r =>
                    decimal.TryParse(r.GetValueOrDefault("receptions"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m);

                var gamesPlayed = g.Count(r =>
                    decimal.TryParse(r.GetValueOrDefault("fantasy_points"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0);

                if (gamesPlayed == 0) return null;

                // Prefer the most recent non-empty team in the group — a traded
                // player's first week should not decide his season row.
                var team = g.Select(r => FirstNonEmpty(r, TeamColumns))
                            .LastOrDefault(t => !string.IsNullOrWhiteSpace(t))
                           ?? string.Empty;

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["player_id"] = FirstNonEmpty(sample, GsisIdColumns) ?? string.Empty,
                    ["player_display_name"] = sample.GetValueOrDefault("player_display_name")
                        ?? sample.GetValueOrDefault("player_name")
                        ?? string.Empty,
                    ["position"] = sample["position"].Trim().ToUpper(),
                    ["recent_team"] = team,
                    ["season_type"] = "REG",
                    ["games"] = gamesPlayed.ToString(CultureInfo.InvariantCulture),
                    ["fantasy_points"] = totalPts.ToString(CultureInfo.InvariantCulture),
                    ["receptions"] = totalRec.ToString(CultureInfo.InvariantCulture),
                };

                return row;
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
    }

    // ── Name normalisation ────────────────────────────────────────────────────
    //
    // Suffix stripping is what makes "Kenneth Walker III" and "Kenneth Walker"
    // the same key — which is correct for matching one player across nflverse
    // files, and is exactly why the key is not unique on its own. Position and
    // the tiebreak ladder above carry that weight now; this method must never be
    // the sole basis for a bind.

    private static string NormalizeName(string name) =>
        name.ToLowerInvariant()
            .Replace(".", "")
            .Replace("'", "")
            .Replace("-", " ")
            .Replace(" jr", "")
            .Replace(" sr", "")
            .Replace(" iv", "")
            .Replace(" iii", "")   // longest suffix first — "ii" below would otherwise eat part of it
            .Replace(" ii", "")
            .Trim();

    // ── CSV ───────────────────────────────────────────────────────────────────

    private static List<Dictionary<string, string>> ParseCsv(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return [];

        var headers = SplitCsvLine(lines[0]);
        var result = new List<Dictionary<string, string>>(lines.Length - 1);

        for (int i = 1; i < lines.Length; i++)
        {
            var values = SplitCsvLine(lines[i]);
            if (values.Length != headers.Length) continue;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < headers.Length; j++)
                row[headers[j].Trim()] = values[j].Trim().Trim('"');

            result.Add(row);
        }

        return result;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }

        result.Add(current.ToString());
        return result.ToArray();
    }
}
