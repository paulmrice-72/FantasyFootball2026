// FF.Application/Stats/Commands/ImportHistoricalStats/ImportHistoricalStatsCommandHandler.cs

using FF.Application.Interfaces.Services;
using FF.Application.Stats.Commands.ImportHistoricalStats;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FF.Application.Stats.Commands.ImportHistoricalStats;

public class ImportHistoricalStatsCommandHandler(
    IHistoricalStatsImportService importService,
    IConfiguration configuration,
    ILogger<ImportHistoricalStatsCommandHandler> logger)
        : IRequestHandler<ImportHistoricalStatsCommand, Result<ImportHistoricalStatsResult>>
{
    private readonly IHistoricalStatsImportService _importService = importService;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<ImportHistoricalStatsCommandHandler> _logger = logger;

    public async Task<Result<ImportHistoricalStatsResult>> Handle(
        ImportHistoricalStatsCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve base path: config value → fallback to Data/Historical
            // relative to the working directory (API project output folder).
            // Override in appsettings.Development.json with an absolute path on PAULMRICE.
            var basePath = _configuration["HistoricalData:BasePath"]
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Data", "Historical");

            _logger.LogInformation(
                "ImportHistoricalStats: basePath={Path}, seasons={Seasons}, pfr={Pfr}",
                basePath,
                request.Seasons == null ? "all" : string.Join(",", request.Seasons),
                request.RunPfrValidation);

            var importResult = await _importService.ImportAsync(
                basePath,
                request.Seasons,
                request.RunPfrValidation,
                cancellationToken);

            var seasonBreakdown = importResult.SeasonResults
                .Select(s => new SeasonSummary(
                    s.Season,
                    s.Inserted,
                    s.Replaced,
                    s.FileNotFound,
                    s.ValidationSummary?.FlaggedPlayers ?? 0))
                .ToList();

            return Result.Success(new ImportHistoricalStatsResult(
                importResult.TotalInserted,
                importResult.TotalReplaced,
                importResult.TotalSkipped,
                seasonBreakdown,
                importResult.ValidationWarnings,
                importResult.Duration
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ImportHistoricalStats command failed");
            return Result.Failure<ImportHistoricalStatsResult>(
                new Error("HistoricalStats.ImportFailed", ex.Message));
        }
    }
}