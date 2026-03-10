// FF.Application/Interfaces/Services/INflverseDownloadService.cs
namespace FF.Application.Interfaces.Services;


public interface INflverseDownloadService
{
    /// <summary>
    /// Downloads the current season player stats CSV from nflverse GitHub releases.
    /// Saves to the configured HistoricalData.BasePath for subsequent import.
    /// </summary>
    Task<NflverseDownloadResult> DownloadCurrentSeasonAsync(
        int season,
        CancellationToken cancellationToken = default);

    Task<NflverseDownloadResult> DownloadSnapCountsAsync(
        int season,
        CancellationToken cancellationToken = default);

    Task<NflverseDownloadResult> DownloadRostersAsync(
        int season,
        CancellationToken cancellationToken = default);
}

public class NflverseDownloadResult
{
    public bool Success { get; init; }
    public int Season { get; init; }
    public string? SavedPath { get; init; }
    public long FileSizeBytes { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}