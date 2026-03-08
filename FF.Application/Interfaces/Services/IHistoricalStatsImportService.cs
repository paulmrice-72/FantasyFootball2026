// FF.Application/Interfaces/Services/IHistoricalStatsImportService.cs
using FF.Application.Stats.Commands;

namespace FF.Application.Interfaces.Services;

public interface IHistoricalStatsImportService
{
    /// <summary>
    /// Full import — used by the API endpoint for initial/manual loads.
    /// Imports specified seasons (or all supported seasons if null).
    /// </summary>
    Task<HistoricalImportResult> ImportAsync(
        string basePath,
        int[]? seasons = null,
        bool runPfrValidation = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Single-season import — used by the Hangfire weekly sync job.
    /// Pulls basePath from config internally so the job needs no parameters.
    /// </summary>
    Task<HistoricalImportResult> ImportSeasonAsync(
        int season,
        CancellationToken cancellationToken = default);
}