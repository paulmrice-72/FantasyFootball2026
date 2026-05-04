// FF.Application/Features/Simulations/Commands/SeedSeasonAverageSims/SeedSeasonAverageSimsCommandHandler.cs
using System.Globalization;
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Simulations.Commands.SeedSeasonAverageSims;

public class SeedSeasonAverageSimsCommandHandler(
    ISimulationResultRepository simRepository,
    IPlayerRepository playerRepository,
    IHttpClientFactory httpClientFactory,
    ILogger<SeedSeasonAverageSimsCommandHandler> logger)
    : IRequestHandler<SeedSeasonAverageSimsCommand, SeedSeasonAverageSimsResult>
{
    private static readonly HashSet<string> SkillPositions = ["QB", "RB", "WR", "TE"];

    // nflverse publishes player_stats_{season}.csv after season ends (typically Feb).
    // Columns relevant to us: season, season_type, player_name, position,
    //   games, receptions, fantasy_points, recent_team
    // Half-PPR = fantasy_points + (receptions * 0.5)
    // Season average = halfPpr / games
    private const string NflverseUrlTemplate =
        "https://github.com/nflverse/nflverse-data/releases/download/player_stats/player_stats_season_{0}.csv";

    public async Task<SeedSeasonAverageSimsResult> Handle(
        SeedSeasonAverageSimsCommand request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("SeedSeasonAverageSims starting for season {Season}", request.Season);

        // 1 — Download CSV
        var url = string.Format(NflverseUrlTemplate, request.Season);
        var http = httpClientFactory.CreateClient("NflverseClient");
        string csv;
        try
        {
            csv = await http.GetStringAsync(url, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download nflverse player_stats for season {Season}", request.Season);
            throw new InvalidOperationException(
                $"Could not download player_stats_{request.Season}.csv from nflverse. " +
                $"The file may not be published yet (typically available in February after season ends).", ex);
        }

        // 2 — Parse CSV
        var rows = ParseCsv(csv);
        // After parsing, validate we got the right file
        if (rows.Count > 0 && rows[0].ContainsKey("week"))
            throw new InvalidOperationException(
                $"Downloaded file appears to be the weekly stats file (contains 'week' column). " +
                $"Expected the season aggregate file player_stats_season_{request.Season}.csv.");

        logger.LogInformation("Parsed {Count} rows from nflverse player_stats_{Season}.csv",
            rows.Count, request.Season);

        // 3 — Filter: REG season, skill positions, games > 0
        var eligible = rows
            .Where(r =>
                r.TryGetValue("season_type", out var st) && st.Trim() == "REG" &&
                r.TryGetValue("position", out var pos) && SkillPositions.Contains(pos.Trim().ToUpper()) &&
                r.TryGetValue("games", out var gamesStr) &&
                decimal.TryParse(gamesStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var gamesVal) &&
                gamesVal > 0)
            .ToList();

        logger.LogInformation("{Count} eligible REG-season rows after filtering", eligible.Count);

        // 4 — Load all Sleeper players for name matching
        var allPlayers = await playerRepository.GetAllAsync(cancellationToken);
        var playerByNormalizedName = allPlayers
            .Where(p => p.SleeperPlayerId != null && p.FullName != null)
            .GroupBy(p => NormalizeName(p.FullName!))
            .ToDictionary(g => g.Key, g => g.First());

        // 5 — Build sim docs
        var toUpsert = new List<SimulationResultDocument>();
        int skipped = 0, unmatched = 0;

        foreach (var row in eligible)
        {
            var playerName = row.GetValueOrDefault("player_display_name")
                          ?? row.GetValueOrDefault("player_name")
                          ?? string.Empty;
            var position = row["position"].Trim().ToUpper();
            var nflTeam = row.GetValueOrDefault("recent_team") ?? string.Empty;

            if (!int.TryParse(row.GetValueOrDefault("games"), out var games) || games <= 0)
            { skipped++; continue; }

            if (!decimal.TryParse(row.GetValueOrDefault("fantasy_points"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var stdPts))
            { skipped++; continue; }

            if (!decimal.TryParse(row.GetValueOrDefault("receptions"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var receptions))
                receptions = 0m;

            var halfPprTotal = stdPts + (receptions * 0.5m);
            var avgHalfPpr = halfPprTotal / games;

            // Name match to Sleeper player
            var normalized = NormalizeName(playerName);
            if (!playerByNormalizedName.TryGetValue(normalized, out var player))
            {
                logger.LogDebug("No Sleeper match for '{Name}' ({Pos})", playerName, position);
                unmatched++;
                continue;
            }

            toUpsert.Add(new SimulationResultDocument
            {
                SleeperPlayerId = player.SleeperPlayerId!,
                PlayerName = player.FullName ?? playerName,
                Position = position,
                NflTeam = nflTeam,
                Season = request.Season,
                Week = 0,              // sentinel: season average
                Median = Math.Round(avgHalfPpr, 2),
                Floor = Math.Round(avgHalfPpr * 0.6m, 2),
                Ceiling = Math.Round(avgHalfPpr * 1.5m, 2),
                Mean = Math.Round(avgHalfPpr, 2),
                BaseProjection = Math.Round(avgHalfPpr, 2),
                StandardDeviation = Math.Round(avgHalfPpr * 0.20m, 2), // ~20% StdDev as approximation
                BoomProbability = 0.20m,
                BustProbability = 0.15m,
                Iterations = 0,              // not a simulation run — seeded from actuals
                ScoringFormat = "HalfPPR",
                CalculatedAt = DateTime.UtcNow,
                OpponentTeam = string.Empty,
                Spread = 0,
                GameScript = "Neutral",
                PlayerRole = "SeasonAverage"
            });
        }

        logger.LogInformation(
            "Upserting {Count} season-average sim docs for season {Season} (skipped={Skip}, unmatched={Unmatched})",
            toUpsert.Count, request.Season, skipped, unmatched);

        await simRepository.UpsertBatchAsync(toUpsert, cancellationToken);

        return new SeedSeasonAverageSimsResult(
            Seeded: toUpsert.Count,
            Skipped: skipped,
            Unmatched: unmatched);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string NormalizeName(string name) =>
        name.ToLowerInvariant()
            .Replace(".", "")
            .Replace("'", "")
            .Replace("-", " ")
            .Replace("jr", "")
            .Replace("sr", "")
            .Replace("ii", "")
            .Replace("iii", "")
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
                // Handle escaped quotes ("")
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // skip next quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString()); // last field
        return result.ToArray();
    }
}