using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Infrastructure.ExternalApis.CsvImport.Parsers;
using FF.Infrastructure.ExternalApis.Nflverse;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class SnapCountImportService(
    INflverseDownloadService nflverseDownloadService,
    ISnapCountRepository snapCountRepository,
    SnapCountCsvParser snapCountCsvParser,
    ILogger<SnapCountImportService> logger
) : ISnapCountImportService
{
    public async Task<SnapCountImportResult> ImportAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting snap count import for season {Season}", season);

        try
        {
            var download = await nflverseDownloadService.DownloadSnapCountsAsync(
                season, cancellationToken);

            if (!download.Success || download.SavedPath is null)
                return new SnapCountImportResult(false, 0, 0,
                    $"Download failed: {download.ErrorMessage}");

            await using var stream = File.OpenRead(download.SavedPath);
            var documents = snapCountCsvParser.Parse(stream, season).ToList();

            logger.LogInformation("Parsed {Count} snap count records for {Season}",
                documents.Count, season);

            if (documents.Count == 0)
                return new SnapCountImportResult(false, 0, 0, "No records parsed from CSV.");

            await snapCountRepository.EnsureIndexesAsync();
            var (inserted, replaced) = await snapCountRepository.UpsertBatchAsync(
                documents, cancellationToken);

            logger.LogInformation(
                "Snap count import complete. Inserted: {Inserted}, Replaced: {Replaced}",
                inserted, replaced);

            return new SnapCountImportResult(true, inserted, replaced, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Snap count import failed for season {Season}", season);
            return new SnapCountImportResult(false, 0, 0, ex.Message);
        }
    }
}