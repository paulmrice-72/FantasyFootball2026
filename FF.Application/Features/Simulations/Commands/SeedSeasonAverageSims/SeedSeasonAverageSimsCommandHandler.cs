// FF.Application/Features/Simulations/Commands/SeedSeasonAverageSims/SeedSeasonAverageSimsCommandHandler.cs
using System.Globalization;
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
            var firstLine = csv.Split('\n').FirstOrDefault() ?? string.Empty;
            isWeeklyFile = firstLine.Contains(",week,", StringComparison.OrdinalIgnoreCase)
                || firstLine.StartsWith("week,", StringComparison.OrdinalIgnoreCase)
                || firstLine.Contains("\"week\"", StringComparison.OrdinalIgnoreCase);
            logger.LogInformation(
                "Using provided CSV content — weeklyFile={IsWeekly}, season {Season}",
                isWeeklyFile, request.Season);
        }
        else
        {
            // Try season aggregate from nflverse first
            var http = httpClientFactory.CreateClient("NflverseClient");
            var seasonUrl = string.Format(SeasonAggregateUrlTemplate, request.Season);
            string? downloaded = null;
            isWeeklyFile = false;

            try
            {
                var response = await http.GetAsync(seasonUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    var firstLine = content.Split('\n').FirstOrDefault() ?? string.Empty;
                    bool looksWeekly = firstLine.Contains(",week,", StringComparison.OrdinalIgnoreCase)
                        || firstLine.StartsWith("week,", StringComparison.OrdinalIgnoreCase)
                        || firstLine.Contains("\"week\"", StringComparison.OrdinalIgnoreCase);
                    if (!looksWeekly)
                    {
                        downloaded = content;
                        logger.LogInformation("Using season aggregate file for {Season}", request.Season);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Season aggregate download failed for {Season} — trying weekly file", request.Season);
            }

            if (downloaded is null)
            {
                // Fall back to weekly file
                var weeklyUrl = string.Format(WeeklyUrlTemplate, request.Season);
                logger.LogInformation(
                    "Season aggregate not available for {Season} — falling back to weekly URL", request.Season);
                try
                {
                    downloaded = await http.GetStringAsync(weeklyUrl, cancellationToken);
                    isWeeklyFile = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Neither season aggregate (player_stats_season_{request.Season}.csv) " +
                        $"nor weekly file (player_stats_{request.Season}.csv) could be downloaded from nflverse. " +
                        $"Upload the CSV directly via the Admin import panel instead.", ex);
                }
            }

            csv = downloaded;
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
                    && r.TryGetValue("games", out var gamesStr)
                    && decimal.TryParse(gamesStr, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var g) && g > 0)
                .ToList();
            logger.LogInformation("{Count} eligible REG-season rows after filtering", eligible.Count);
        }

        // ── 4: Load Sleeper players — GsisId match is primary, normalized name is fallback ──
        var gsisToSleeper = await resolutionService.BuildGsisToSleeperMapAsync(cancellationToken);
        
        var allPlayers = await playerRepository.GetAllAsync(cancellationToken);
        var playerBySleeperId = allPlayers
                    .Where(p => p.SleeperPlayerId != null)
                    .GroupBy(p => p.SleeperPlayerId!)
                    .ToDictionary(g => g.Key, g => g.First());

        var playerByNormalizedName = allPlayers
            .Where(p => p.SleeperPlayerId != null && p.FullName != null)
            .GroupBy(p => NormalizeName(p.FullName!))
            .ToDictionary(g => g.Key, g => g.First());

        // ── 5: Build sim docs ─────────────────────────────────────────────────
        var toUpsert = new List<SimulationResultDocument>();
        int skipped = 0, unmatched = 0, matchedByGsis = 0, matchedByName = 0;

        foreach (var row in eligible)
        {
            var playerName = row.GetValueOrDefault("player_display_name")
                ?? row.GetValueOrDefault("player_name")
                ?? string.Empty;
            var position = row["position"].Trim().ToUpper();
            var nflTeam = row.GetValueOrDefault("recent_team")
                            ?? row.GetValueOrDefault("team")   // weekly file (Jul 2026+) renamed this column
                            ?? string.Empty;
            var gsisId = row.GetValueOrDefault("player_id");

            if (!int.TryParse(row.GetValueOrDefault("games"), out var games) || games <= 0)
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
            var avgHalfPpr = halfPprTotal / games;

            FF.Domain.Entities.Player? player = null;

            if (!string.IsNullOrWhiteSpace(gsisId)
                && gsisToSleeper.TryGetValue(gsisId, out var sleeperId)
                && playerBySleeperId.TryGetValue(sleeperId, out player))
            {
                matchedByGsis++;
            }
            else
            {
                var normalized = NormalizeName(playerName);
                if (playerByNormalizedName.TryGetValue(normalized, out player))
                {
                    matchedByName++;
                }
                else
                {
                    logger.LogDebug("No Sleeper match for '{Name}' ({Pos}) — GsisId={Gsis}",
                        playerName, position, gsisId);
                    unmatched++;
                    continue;
                }
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
                PlayerRole = "SeasonAverage"
            });
        }

        logger.LogInformation(
            "Upserting {Count} season-average sim docs for season {Season} " +
            "(byGsis={Gsis}, byName={Name}, skipped={Skip}, unmatched={Unmatched})",
                toUpsert.Count, request.Season, matchedByGsis, matchedByName, skipped, unmatched);

        await simRepository.UpsertBatchAsync(toUpsert, cancellationToken);

        return new SeedSeasonAverageSimsResult(
            Seeded: toUpsert.Count,
            Skipped: skipped,
            Unmatched: unmatched);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
                var name = r.GetValueOrDefault("player_display_name")
                    ?? r.GetValueOrDefault("player_name")
                    ?? string.Empty;
                var pos = r["position"].Trim().ToUpper();
                return $"{name}|{pos}";
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

                return new Dictionary<string, string>
                {
                    ["player_id"] = sample.GetValueOrDefault("player_id") ?? string.Empty,
                    ["player_display_name"] = sample.GetValueOrDefault("player_display_name")
                        ?? sample.GetValueOrDefault("player_name")
                        ?? string.Empty,
                    ["position"] = sample["position"].Trim().ToUpper(),
                    ["recent_team"] = sample.GetValueOrDefault("recent_team") ?? sample.GetValueOrDefault("team") ?? string.Empty,
                    ["season_type"] = "REG",
                    ["games"] = gamesPlayed.ToString(),
                    ["fantasy_points"] = totalPts.ToString(CultureInfo.InvariantCulture),
                    ["receptions"] = totalRec.ToString(CultureInfo.InvariantCulture),
                };
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
    }

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