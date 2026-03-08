// FF.Application/Interfaces/Services/IHistoricalStatsImportService.cs
//
// Application layer interface for the historical stats import pipeline.
// Concrete implementation is FF.Infrastructure/Services/HistoricalStatsImportService.cs
// Registered in FF.Infrastructure/DependencyInjection.cs.

using FF.Application.Stats.Commands;

namespace FF.Application.Interfaces.Services;

public interface IHistoricalStatsImportService
{
    Task<HistoricalImportResult> ImportAsync(
        string basePath,
        int[]? seasons = null,
        bool runPfrValidation = true,
        CancellationToken cancellationToken = default);
}