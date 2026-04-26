// FF.Infrastructure/ExternalApis/Nflverse/NflverseDownloadService.cs
using FF.Application.Common.Settings;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        "https://github.com/nflverse/nflverse-data/releases/download/player_stats";

    public async Task<NflverseDownloadResult> DownloadCurrentSeasonAsync(
        int season, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        // Try current season first, fall back to prior year
        // nflverse publishes season aggregate ~4-8 weeks post-Super Bowl
        // New naming convention (2025+): player_stats_season_{year}.csv
        // Legacy naming (pre-2025): player_stats_{year}.csv
        var seasonsToTry = new[]
        {
            (season,     $"{BaseUrl}/player_stats_season_{season}.csv"),
            (season - 1, $"{BaseUrl}/player_stats_season_{season - 1}.csv"),
            (season - 1, $"{BaseUrl}/player_stats_{season - 1}.csv"),  // legacy fallback
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
}