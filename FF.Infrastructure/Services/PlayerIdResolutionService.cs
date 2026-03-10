using CsvHelper;
using CsvHelper.Configuration;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Infrastructure.ExternalApis.Nflverse.Dtos;
using FF.Infrastructure.Persistence.Mongo.Repositories;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace FF.Infrastructure.Services;

public class PlayerIdResolutionService(
    FFDbContext dbContext,
    IPlayerGameLogRepository playerGameLogRepository,
    INflverseDownloadService downloadService,
    ILogger<PlayerIdResolutionService> logger) : IPlayerIdResolutionService
{
    private readonly FFDbContext _dbContext = dbContext;
    private readonly IPlayerGameLogRepository _playerGameLogRepository = playerGameLogRepository;
    private readonly ILogger<PlayerIdResolutionService> _logger = logger;
    private readonly INflverseDownloadService _downloadService = downloadService;

    public async Task<Dictionary<string, string>> BuildGsisToSleeperMapAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building GsisId → SleeperPlayerId map from nflverse rosters");

        var map = new Dictionary<string, string>();
        var seasons = new[] { 2022, 2023, 2024, 2025 };

        foreach (var season in seasons)
        {
            var result = await _downloadService.DownloadRostersAsync(season, cancellationToken);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to download roster CSV for {Season}", season);
                continue;
            }

            var rows = ParseRosterCsv(result.SavedPath!);
            var added = 0;

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.GsisId) ||
                    string.IsNullOrWhiteSpace(row.SleeperId))
                    continue;

                // TryAdd — first season win, no duplicates thrown
                if (map.TryAdd(row.GsisId, row.SleeperId))
                    added++;
            }

            _logger.LogInformation(
                "Season {Season} roster CSV added {Count} new gsis→sleeper mappings",
                season, added);
        }

        _logger.LogInformation(
            "Built gsis → Sleeper map with {Count} total entries", map.Count);

        return map;
    }


    public async Task<PlayerIdResolutionResult> BackfillMissingSleeperIdsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting SleeperPlayerId backfill on PlayerGameLogs");

        var gsisToSleeper = await BuildGsisToSleeperMapAsync(cancellationToken);

        var unresolved = await _playerGameLogRepository
            .GetDocumentsWithNullSleeperIdAsync(cancellationToken);

        _logger.LogInformation("Found {Count} documents with null SleeperPlayerId", unresolved.Count);

        var resolved = 0;
        var unresolvedCount = 0;
        var unresolvedByPosition = new Dictionary<string, int>();

        foreach (var doc in unresolved)
        {
            if (gsisToSleeper.TryGetValue(doc.PlayerId, out var sleeperId))
            {
                await _playerGameLogRepository.UpdateSleeperPlayerIdAsync(
                    doc.PlayerId, sleeperId, cancellationToken);
                resolved++;
            }
            else
            {
                unresolvedCount++;
                var pos = doc.Position ?? "Unknown";
                unresolvedByPosition[pos] = unresolvedByPosition.GetValueOrDefault(pos) + 1;
            }
        }

        _logger.LogInformation(
            "Backfill complete. Resolved: {Resolved}, Unresolved: {Unresolved}",
            resolved, unresolvedCount);

        return new PlayerIdResolutionResult(
            unresolved.Count,
            resolved,
            unresolvedCount,
            unresolvedByPosition);
    }

    private static List<NflverseRosterRow> ParseRosterCsv(string filePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,   // ignore missing columns
            MissingFieldFound = null  // ignore missing fields
        };

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        return [.. csv.GetRecords<NflverseRosterRow>()];
    }
}