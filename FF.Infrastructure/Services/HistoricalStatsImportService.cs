// FF.Infrastructure/Services/HistoricalStatsImportService.cs
//
// Orchestrates the full historical stats import pipeline.
// Called by the API endpoint (initial load) and Hangfire job (weekly updates).
//
// PIPELINE:
//   1. Scan Data/Historical/nflfastr/ for CSV files matching seasons requested
//   2. Parse each file with NflfastrCsvParser (streaming, position-filtered)
//   3. Upsert documents to MongoDB in batches of 500
//   4. If PFR files present, run PfrValidationService cross-check
//   5. Return ImportResult with counts and any validation warnings
//
// IDEMPOTENCY:
//   MongoDB upsert on (PlayerId, Season, Week) means re-running is safe.
//   Existing documents are replaced, not duplicated.
//
// DATA PATH CONFIGURATION:
//   appsettings.json:  "HistoricalData": { "BasePath": "Data/Historical" }
//   Relative to the API project output directory.
//   On LINUXSERVER deployment: configure as absolute path to mounted volume.

using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Application.Stats.Commands;
using FF.Infrastructure.ExternalApis.CsvImport;
using FF.Infrastructure.ExternalApis.CsvImport.Parsers;
using FF.Infrastructure.Persistence.Mongo.Repositories;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class HistoricalStatsImportService(
    NflfastrCsvParser nflfastrParser,
    PfrValidationService pfrValidation,
    IPlayerGameLogRepository gameLogRepo,
    ILogger<HistoricalStatsImportService> logger) : IHistoricalStatsImportService
{
    private readonly NflfastrCsvParser _nflfastrParser = nflfastrParser;
    private readonly PfrValidationService _pfrValidation = pfrValidation;
    private readonly IPlayerGameLogRepository _gameLogRepo = gameLogRepo;
    private readonly ILogger<HistoricalStatsImportService> _logger = logger;

    private const int BatchSize = 500;
    private static readonly int[] SupportedSeasons = [2022, 2023, 2024];

    /// <summary>
    /// Imports historical stats for specified seasons (or all supported seasons if null).
    /// Validates against PFR if PFR files are present.
    /// </summary>
    public async Task<HistoricalImportResult> ImportAsync(
        string basePath,
        int[]? seasons = null,
        bool runPfrValidation = true,
        CancellationToken cancellationToken = default)
    {
        var seasonsToImport = seasons ?? SupportedSeasons;
        var result = new HistoricalImportResult();
        var startedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Starting historical stats import: seasons={Seasons}, basePath={Path}",
            string.Join(",", seasonsToImport), basePath);

        foreach (var season in seasonsToImport)
        {
            var seasonResult = await ImportSeasonAsync(
                basePath, season, runPfrValidation, cancellationToken);

            result.SeasonResults.Add(seasonResult);
            result.TotalInserted += seasonResult.Inserted;
            result.TotalReplaced += seasonResult.Replaced;
            result.TotalSkipped += seasonResult.Skipped;

            if (seasonResult.ValidationSummary?.FlaggedPlayers > 0)
                result.ValidationWarnings.AddRange(seasonResult.ValidationSummary.FlaggedPlayerNames);
        }

        result.Duration = DateTime.UtcNow - startedAt;
        result.CompletedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Historical import complete: {Inserted} inserted, {Replaced} replaced, " +
            "{Skipped} skipped, {Warnings} warnings, duration={Duration}",
            result.TotalInserted, result.TotalReplaced, result.TotalSkipped,
            result.ValidationWarnings.Count, result.Duration);

        return result;
    }

    private async Task<SeasonImportResult> ImportSeasonAsync(
        string basePath,
        int season,
        bool runPfrValidation,
        CancellationToken cancellationToken)
    {
        var result = new SeasonImportResult { Season = season };
        var nflfastrPath = Path.Combine(basePath, "nflfastr", $"player_stats_{season}.csv");

        if (!File.Exists(nflfastrPath))
        {
            _logger.LogWarning("nflfastR file not found for season {Season}: {Path}", season, nflfastrPath);
            result.FileNotFound = true;
            return result;
        }

        // Parse CSV
        var documents = await _nflfastrParser.ParseFileAsync(nflfastrPath);
        result.Skipped = 0; // parser handles its own skipping internally

        // Upsert to MongoDB in batches
        for (int i = 0; i < documents.Count; i += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = documents.Skip(i).Take(BatchSize);
            var (inserted, replaced) = await _gameLogRepo.UpsertBatchAsync(batch, cancellationToken);
            result.Inserted += inserted;
            result.Replaced += replaced;

            _logger.LogDebug(
                "Season {Season} batch {Batch}/{Total}: {Inserted} inserted, {Replaced} replaced",
                season, (i / BatchSize) + 1, (int)Math.Ceiling((double)documents.Count / BatchSize),
                inserted, replaced);
        }

        _logger.LogInformation(
            "Season {Season} import: {Inserted} inserted, {Replaced} replaced from {Total} records",
            season, result.Inserted, result.Replaced, documents.Count);

        // PFR validation (optional, non-blocking)
        if (runPfrValidation)
        {
            var pfrPath = Path.Combine(basePath, "pfr", $"pfr_fantasy_{season}.csv");
            if (File.Exists(pfrPath))
            {
                try
                {
                    result.ValidationSummary = await _pfrValidation.ValidateSeasonAsync(
                        pfrPath, season, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PFR validation failed for season {Season} — continuing", season);
                }
            }
            else
            {
                _logger.LogInformation("PFR file not found for {Season} — skipping validation", season);
            }
        }

        return result;
    }
}

// HistoricalImportResult and SeasonImportResult are defined in:
// FF.Application/Stats/Commands/ImportHistoricalStats/HistoricalImportResult.cs