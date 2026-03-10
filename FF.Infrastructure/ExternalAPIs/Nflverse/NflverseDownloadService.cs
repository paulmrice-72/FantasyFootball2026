// FF.Infrastructure/ExternalApis/Nflverse/NflverseDownloadService.cs
using FF.Application.Common.Settings;
using FF.Application.Interfaces.Services;
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

    // Stable nflverse GitHub releases URL — updated automatically each week
    private const string BaseUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/player_stats";

    public async Task<NflverseDownloadResult> DownloadCurrentSeasonAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var url = $"{BaseUrl}/player_stats_{season}.csv";
        var savePath = Path.Combine(
            _settings.BasePath, "nflfastr", $"player_stats_{season}.csv");

        _logger.LogInformation(
            "Downloading nflverse player stats for season {Season} from {Url}",
            season, url);

        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);

            var duration = DateTime.UtcNow - startedAt;

            _logger.LogInformation(
                "Downloaded player_stats_{Season}.csv — {Size:N0} bytes in {Duration}",
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
                "Failed to download nflverse player stats for season {Season}", season);

            return new NflverseDownloadResult
            {
                Success = false,
                Season = season,
                ErrorMessage = ex.Message,
                Duration = DateTime.UtcNow - startedAt
            };
        }
    }

    private const string SnapCountsBaseUrl =
    "https://github.com/nflverse/nflverse-data/releases/download/snap_counts";

    public async Task<NflverseDownloadResult> DownloadSnapCountsAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var url = $"{SnapCountsBaseUrl}/snap_counts_{season}.csv";
        var savePath = Path.Combine(
            _settings.BasePath, "nflfastr", $"snap_counts_{season}.csv");

        _logger.LogInformation(
            "Downloading nflverse snap counts for season {Season} from {Url}",
            season, url);

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
        int season,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var url = $"{RostersBaseUrl}/roster_{season}.csv";
        var savePath = Path.Combine(
            _settings.BasePath, "nflfastr", $"roster_{season}.csv");

        _logger.LogInformation(
            "Downloading nflverse rosters for season {Season} from {Url}",
            season, url);

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
}