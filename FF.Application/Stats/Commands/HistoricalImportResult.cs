// FF.Application/Stats/Commands/ImportHistoricalStats/HistoricalImportResult.cs
//
// Result types for the historical stats import pipeline.
// Defined in FF.Application so both:
//   - IHistoricalStatsImportService (FF.Application) can reference them
//   - HistoricalStatsImportService (FF.Infrastructure) can reference them
//
// FF.Infrastructure references FF.Application — this is the correct direction.
// PfrValidationSummary also lives here for the same reason.

namespace FF.Application.Stats.Commands;

public class HistoricalImportResult
{
    public List<SeasonImportResult> SeasonResults { get; set; } = [];
    public int TotalInserted { get; set; }
    public int TotalReplaced { get; set; }
    public int TotalSkipped { get; set; }
    public List<string> ValidationWarnings { get; set; } = [];
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public bool HasWarnings => ValidationWarnings.Count != 0;
}

public class SeasonImportResult
{
    public int Season { get; set; }
    public int Inserted { get; set; }
    public int Replaced { get; set; }
    public int Skipped { get; set; }
    public bool FileNotFound { get; set; }
    public PfrValidationSummary? ValidationSummary { get; set; }
}

public class PfrValidationSummary
{
    public int Season { get; set; }
    public int MatchedPlayers { get; set; }
    public int ValidatedPlayers { get; set; }
    public int FlaggedPlayers { get; set; }
    public int UnmatchedPlayers { get; set; }
    public List<string> FlaggedPlayerNames { get; set; } = [];
    public bool HasCriticalIssues => FlaggedPlayers > 10;
}