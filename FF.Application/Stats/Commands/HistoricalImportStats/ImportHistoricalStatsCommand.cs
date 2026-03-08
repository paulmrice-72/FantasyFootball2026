// FF.Application/Stats/Commands/ImportHistoricalStats/ImportHistoricalStatsCommand.cs

using FF.SharedKernel;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Stats.Commands.HistoricalImportStats;

/// <summary>
/// Triggers the full historical stats import pipeline.
/// Seasons: null = all supported (2022–2024). Specify array to import a single season.
/// RunPfrValidation: set false to skip PFR cross-check (faster, used by weekly sync job).
/// </summary>
public record ImportHistoricalStatsCommand(
    int[]? Seasons = null,
    bool RunPfrValidation = true
) : IRequest<Result<ImportHistoricalStatsResult>>;

public record ImportHistoricalStatsResult(
    int TotalInserted,
    int TotalReplaced,
    int TotalSkipped,
    List<SeasonSummary> SeasonBreakdown,
    List<string> ValidationWarnings,
    TimeSpan Duration
);

public record SeasonSummary(
    int Season,
    int Inserted,
    int Replaced,
    bool FileNotFound,
    int PfrFlagged
);