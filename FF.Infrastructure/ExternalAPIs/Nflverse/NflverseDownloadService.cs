// FF.Infrastructure/ExternalApis/Nflverse/NflverseDownloadService.cs
using FF.Application.Common.Settings;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace FF.Infrastructure.ExternalApis.Nflverse;

public class NflverseDownloadService(
    HttpClient httpClient,
    IOptions<HistoricalDataSettings> options,
    ILogger<NflverseDownloadService> logger) : INflverseDownloadService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly HistoricalDataSettings _settings = options.Value;
    private readonly ILogger<NflverseDownloadService> _logger = logger;

    private const string BaseUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/stats_player";

    // nflverse renamed the release tag from "player_stats" to "stats_player"
    // in their Jul 2026 rebuild (nflfastR::calculate_stats()). Old tag 404s permanently.

     public async Task<NflverseDownloadResult> DownloadCurrentSeasonAsync(
         int season, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        // This parser (NflfastrCsvParser) needs per-week rows, so the weekly
        // file is primary. Fall back to prior season, then to the legacy
        // pre-rebuild filenames in case an old tag/asset is ever restored.
        var seasonsToTry = new[]
        {
            (season,     $"{BaseUrl}/stats_player_week_{season}.csv"),
            (season - 1, $"{BaseUrl}/stats_player_week_{season - 1}.csv"),
            (season - 1, $"https://github.com/nflverse/nflverse-data/releases/download/player_stats/player_stats_{season - 1}.csv"),  // legacy fallback
        };

        foreach (var (s, url) in seasonsToTry)
        {
            try
            {
                _logger.LogInformation(
                    "Trying nflverse player stats {Season} from {Url}", s, url);

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "player_stats_{Season} returned {Status} — trying next",
                        s, response.StatusCode);
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var savePath = Path.Combine(
                    _settings.BasePath, "nflfastr", $"player_stats_{s}.csv");
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);

                var duration = DateTime.UtcNow - startedAt;
                _logger.LogInformation(
                    "Downloaded player stats season {Season} — {Size:N0} bytes in {Duration}",
                    s, bytes.Length, duration);

                return new NflverseDownloadResult
                {
                    Success = true,
                    Season = s,
                    SavedPath = savePath,
                    FileSizeBytes = bytes.Length,
                    Duration = duration
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed attempting player_stats_{Season} — trying next", s);
            }
        }

        // All attempts exhausted
        _logger.LogError(
            "No player stats data available for season {Season} or prior", season);
        return new NflverseDownloadResult
        {
            Success = false,
            Season = season,
            ErrorMessage = $"player_stats not found for {season} or {season - 1}",
            Duration = DateTime.UtcNow - startedAt
        };
    }

    private const string SnapCountsBaseUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/snap_counts";

    public async Task<NflverseDownloadResult> DownloadSnapCountsAsync(
        int season, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var url = $"{SnapCountsBaseUrl}/snap_counts_{season}.csv";
        var savePath = Path.Combine(
            _settings.BasePath, "nflfastr", $"snap_counts_{season}.csv");

        _logger.LogInformation(
            "Downloading nflverse snap counts for season {Season} from {Url}", season, url);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);
            var duration = DateTime.UtcNow - startedAt;
            _logger.LogInformation(
                "Downloaded snap_counts_{Season}.csv — {Size:N0} bytes in {Duration}",
                season, bytes.Length, duration);
            return new NflverseDownloadResult
            {
                Success = true,
                Season = season,
                SavedPath = savePath,
                FileSizeBytes = bytes.Length,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to download nflverse snap counts for season {Season}", season);
            return new NflverseDownloadResult
            {
                Success = false,
                Season = season,
                ErrorMessage = ex.Message,
                Duration = DateTime.UtcNow - startedAt
            };
        }
    }

    private const string RostersBaseUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/rosters";

    public async Task<NflverseDownloadResult> DownloadRostersAsync(
        int season, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var url = $"{RostersBaseUrl}/roster_{season}.csv";
        var savePath = Path.Combine(
            _settings.BasePath, "nflfastr", $"roster_{season}.csv");

        _logger.LogInformation(
            "Downloading nflverse rosters for season {Season} from {Url}", season, url);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);
            var duration = DateTime.UtcNow - startedAt;
            _logger.LogInformation(
                "Downloaded roster_{Season}.csv — {Size:N0} bytes in {Duration}",
                season, bytes.Length, duration);
            return new NflverseDownloadResult
            {
                Success = true,
                Season = season,
                SavedPath = savePath,
                FileSizeBytes = bytes.Length,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to download nflverse rosters for season {Season}", season);
            return new NflverseDownloadResult
            {
                Success = false,
                Season = season,
                ErrorMessage = ex.Message,
                Duration = DateTime.UtcNow - startedAt
            };
        }
    }

    // ── Depth Charts ────────────────────────────────────────────────────────────
    // Actual nflverse depth_charts CSV columns (2025/2026 format):
    // dt, team, player_name, espn_id, gsis_id, pos_grp_id, pos_grp,
    // pos_id, pos_name, pos_abb, pos_slot, pos_rank

    private const string DepthChartsBaseUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/depth_charts";

    public async Task<IReadOnlyList<DepthChartDocument>> DownloadDepthChartsAsync(
        int season, CancellationToken cancellationToken = default)
    {
        // Try current season, fall back to prior if not found
        var seasonsToTry = new[] { season, season - 1 };

        foreach (var s in seasonsToTry)
        {
            var url = $"{DepthChartsBaseUrl}/depth_charts_{s}.csv";
            _logger.LogInformation(
                "Downloading nflverse depth charts season {Season} from {Url}", s, url);

            try
            {
                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "depth_charts_{Season}.csv returned {Status} — trying prior season",
                        s, response.StatusCode);
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var rows = ParseDepthChartsCsv(content, s);

                if (rows.Count == 0)
                {
                    _logger.LogWarning(
                        "depth_charts_{Season}.csv parsed 0 rows — trying prior season", s);
                    continue;
                }

                _logger.LogInformation(
                    "Parsed {Count} depth chart rows for season {Season}", rows.Count, s);
                return rows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to download depth charts for season {Season}", s);
            }
        }

        _logger.LogWarning("No depth chart data available for season {Season} or prior", season);
        return [];
    }

    private List<DepthChartDocument> ParseDepthChartsCsv(string csv, int season)
    {
        var results = new List<DepthChartDocument>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return results;

        // Real columns: dt,team,player_name,espn_id,gsis_id,pos_grp_id,pos_grp,
        //               pos_id,pos_name,pos_abb,pos_slot,pos_rank
        var headers = SplitCsvLine(lines[0].Trim());
        int idxTeam = Array.IndexOf(headers, "team");
        int idxName = Array.IndexOf(headers, "player_name");
        int idxGsis = Array.IndexOf(headers, "gsis_id");
        int idxPosAbb = Array.IndexOf(headers, "pos_abb");      // "QB", "WR1", "LDE" etc
        int idxPosGrp = Array.IndexOf(headers, "pos_grp");      // "Shotgun", "Base 4-3 D" etc
        int idxPosRank = Array.IndexOf(headers, "pos_rank");     // 1=starter, 2=backup
        int idxPosName = Array.IndexOf(headers, "pos_name");     // "Left Defensive End" etc

        _logger.LogInformation(
            "Depth chart headers found — team:{T} name:{N} gsis:{G} posAbb:{A} rank:{R}",
            idxTeam, idxName, idxGsis, idxPosAbb, idxPosRank);

        foreach (var line in lines.Skip(1))
        {
            var cols = SplitCsvLine(line.Trim());
            if (cols.Length < 5) continue;

            var gsisId = SafeGet(cols, idxGsis);
            if (string.IsNullOrEmpty(gsisId)) continue;

            _ = int.TryParse(SafeGet(cols, idxPosRank), out var posRank);

            results.Add(new DepthChartDocument
            {
                Season = season,
                Week = 0,              // this file is a snapshot, not weekly
                GsisId = gsisId,
                FullName = SafeGet(cols, idxName),
                NflTeam = SafeGet(cols, idxTeam),
                Position = SafeGet(cols, idxPosAbb),    // abbreviated position
                DepthTeam = posRank,                      // 1=starter, 2=backup
                DepthPosition = SafeGet(cols, idxPosName),   // full position name
                FormationPosition = SafeGet(cols, idxPosGrp),  // formation group
                SyncedAt = DateTime.UtcNow
            });
        }

        return results;
    }

    private string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { result.Add(current.ToString().Trim()); current.Clear(); }
            else { current.Append(c); }
        }
        result.Add(current.ToString().Trim());
        return [.. result];
    }

    private static string SafeGet(string[] cols, int idx) =>
        idx >= 0 && idx < cols.Length ? cols[idx].Trim('"', ' ') : string.Empty;

    // ── Schedule (FAN-178) ────────────────────────────────────────────────

    /// <summary>
    /// One file, every season since 1999. Not a per-season release asset like
    /// the other nflverse feeds, so it is fetched whole and filtered in memory.
    /// </summary>
    private const string ScheduleUrl =
        "https://raw.githubusercontent.com/nflverse/nfldata/master/data/games.csv";

    public async Task<IReadOnlyList<NflScheduleDocument>> DownloadScheduleAsync(
        int season, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Downloading nflverse schedule for season {Season} from {Url}", season, ScheduleUrl);

        try
        {
            var response = await _httpClient.GetAsync(ScheduleUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "games.csv returned {Status} — schedule unavailable for season {Season}",
                    response.StatusCode, season);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var games = ParseScheduleCsv(content, season);

            if (games.Count == 0)
            {
                // Deliberately not a fallback to season-1. A schedule for the
                // wrong season is worse than none: it would look valid, join
                // cleanly, and put every player against the wrong opponent —
                // which is the exact defect FAN-178 exists to fix.
                _logger.LogError(
                    "games.csv parsed 0 REG games for season {Season}. nflverse has not " +
                    "published it yet, or the column layout changed.", season);
                return [];
            }

            // A full regular season is 272 games (17 weeks x 16). Anything short
            // is a partial publish, which is survivable but must not pass silently.
            if (games.Count < 272)
            {
                _logger.LogWarning(
                    "Schedule for {Season} parsed only {Count} of the expected 272 REG games — " +
                    "treating as a partial publish. Weeks present: {Weeks}",
                    season, games.Count,
                    string.Join(",", games.Select(g => g.Week).Distinct().Order()));
            }

            // Every team must appear, or a join against it silently yields no
            // game and the player renders as though on a bye all season.
            var seen = games
                .SelectMany(g => new[] { g.HomeTeam, g.AwayTeam })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = NflTeamNormalizer.CanonicalTeams
                .Where(t => !seen.Contains(t))
                .ToList();

            if (missing.Count > 0)
            {
                _logger.LogError(
                    "Schedule for {Season} is missing {Count} team(s): {Missing}. This is an " +
                    "abbreviation mismatch, not a real absence — every team plays every season.",
                    season, missing.Count, string.Join(", ", missing));
            }

            _logger.LogInformation(
                "Parsed {Count} REG games for season {Season} across weeks {Min}-{Max}",
                games.Count, season, games.Min(g => g.Week), games.Max(g => g.Week));

            return games;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download schedule for season {Season}", season);
            return [];
        }
    }

    private List<NflScheduleDocument> ParseScheduleCsv(string csv, int season)
    {
        var results = new List<NflScheduleDocument>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return results;

        var headers = SplitCsvLine(lines[0].Trim());

        int idxGameId = Array.IndexOf(headers, "game_id");
        int idxSeason = Array.IndexOf(headers, "season");
        int idxGameType = Array.IndexOf(headers, "game_type");
        int idxWeek = Array.IndexOf(headers, "week");
        int idxGameday = Array.IndexOf(headers, "gameday");
        int idxWeekday = Array.IndexOf(headers, "weekday");
        int idxGametime = Array.IndexOf(headers, "gametime");
        int idxAway = Array.IndexOf(headers, "away_team");
        int idxHome = Array.IndexOf(headers, "home_team");
        int idxAwayScore = Array.IndexOf(headers, "away_score");
        int idxHomeScore = Array.IndexOf(headers, "home_score");
        int idxLocation = Array.IndexOf(headers, "location");

        // spread_line is the closing line from the HOME team's perspective and
        // total_line the closing over/under. Note the neighbouring "total" and
        // "result" columns are the ACTUAL combined score and home margin — using
        // those as a line would silently feed the game-script classifier the
        // outcome instead of the expectation.
        int idxSpreadLine = Array.IndexOf(headers, "spread_line");
        int idxTotalLine = Array.IndexOf(headers, "total_line");

        // If the feed's layout ever changes, fail loudly here rather than
        // producing rows with empty teams that join to nothing.
        if (idxGameId < 0 || idxSeason < 0 || idxWeek < 0 || idxHome < 0 || idxAway < 0)
        {
            _logger.LogError(
                "games.csv is missing a required column — game_id:{G} season:{S} week:{W} " +
                "home_team:{H} away_team:{A}. Refusing to parse.",
                idxGameId, idxSeason, idxWeek, idxHome, idxAway);
            return results;
        }

        foreach (var line in lines.Skip(1))
        {
            var cols = SplitCsvLine(line.Trim());
            if (cols.Length < 10) continue;

            if (!int.TryParse(SafeGet(cols, idxSeason), out var rowSeason) || rowSeason != season)
                continue;

            var gameType = SafeGet(cols, idxGameType);
            if (!gameType.Equals("REG", StringComparison.OrdinalIgnoreCase)) continue;

            if (!int.TryParse(SafeGet(cols, idxWeek), out var week)) continue;

            var gamedayRaw = SafeGet(cols, idxGameday);
            if (!DateTime.TryParseExact(
                    gamedayRaw, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var gameday))
            {
                _logger.LogWarning(
                    "Skipping {GameId} — unparseable gameday '{Raw}'",
                    SafeGet(cols, idxGameId), gamedayRaw);
                continue;
            }

            results.Add(new NflScheduleDocument
            {
                GameId = SafeGet(cols, idxGameId),
                Season = rowSeason,
                Week = week,
                GameType = "REG",
                Gameday = DateTime.SpecifyKind(gameday.Date, DateTimeKind.Utc),
                GametimeEt = SafeGet(cols, idxGametime),
                Weekday = SafeGet(cols, idxWeekday),

                // Normalised at the boundary — the feed writes LA, every roster
                // join in this system uses LAR.
                HomeTeam = NflTeamNormalizer.Normalize(SafeGet(cols, idxHome)),
                AwayTeam = NflTeamNormalizer.Normalize(SafeGet(cols, idxAway)),

                HomeScore = ParseNullableInt(SafeGet(cols, idxHomeScore)),
                AwayScore = ParseNullableInt(SafeGet(cols, idxAwayScore)),
                SpreadLine = ParseNullableDecimal(SafeGet(cols, idxSpreadLine)),
                TotalLine = ParseNullableDecimal(SafeGet(cols, idxTotalLine)),

                Location = SafeGet(cols, idxLocation) is { Length: > 0 } loc ? loc : "Home",
                SyncedAt = DateTime.UtcNow
            });
        }

        return results;
    }

    private static int? ParseNullableInt(string raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    private static decimal? ParseNullableDecimal(string raw) =>
        decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
}