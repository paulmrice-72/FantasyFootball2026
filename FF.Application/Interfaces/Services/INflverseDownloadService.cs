// FF.Application/Interfaces/Services/INflverseDownloadService.cs
using FF.Domain.Documents;

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
    Task<IReadOnlyList<DepthChartDocument>> DownloadDepthChartsAsync(int season, CancellationToken ct = default);

    /// <summary>
    /// Downloads the nflverse schedule and returns the REG-season games for
    /// <paramref name="season"/>, teams already normalised (FAN-178).
    ///
    /// <para>
    /// Unlike the other downloads here, the source is a single all-seasons file
    /// (<c>nfldata/data/games.csv</c>, ~2MB, 1999-present) rather than a
    /// per-season release asset, so there is no prior-season fallback: either
    /// the requested season is in the file or it has not been published. The
    /// full forward schedule is present before Week 1 kicks off, which is what
    /// makes this usable for future-week projections.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<NflScheduleDocument>> DownloadScheduleAsync(
        int season, CancellationToken ct = default);
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