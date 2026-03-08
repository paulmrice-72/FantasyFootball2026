// FF.Application/Stats/Queries/GetDataQuality/DataQualityReport.cs
//
// Domain object representing the result of a data quality validation run.
// Checks completeness, range validity, and cross-season consistency
// of PlayerGameLog documents in MongoDB.

namespace FF.Application.Stats.Queries.GetDataQuality;

public class DataQualityReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DataQualityStatus OverallStatus { get; set; }
    public List<SeasonQualityResult> SeasonResults { get; set; } = [];
    public List<DataQualityIssue> Issues { get; set; } = [];

    // Summary counts
    public int TotalDocuments { get; set; }
    public int TotalIssues => Issues.Count;
    public int CriticalIssues => Issues.Count(x => x.Severity == IssueSeverity.Critical);
    public int WarningIssues => Issues.Count(x => x.Severity == IssueSeverity.Warning);
    public bool IsHealthy => OverallStatus == DataQualityStatus.Healthy;
}

public class SeasonQualityResult
{
    public int Season { get; set; }
    public long DocumentCount { get; set; }
    public long ExpectedMinimum { get; set; }
    public long ExpectedMaximum { get; set; }
    public bool CountInRange => DocumentCount >= ExpectedMinimum
                             && DocumentCount <= ExpectedMaximum;
    public int IssuesFound { get; set; }
    public DataQualityStatus Status { get; set; }

    // Position breakdown
    public Dictionary<string, long> CountByPosition { get; set; } = [];
}

public class DataQualityIssue
{
    public string Rule { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; }
    public int? Season { get; set; }
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public string? FieldName { get; set; }
    public string? ActualValue { get; set; }
    public string? ExpectedRange { get; set; }
}

public enum DataQualityStatus
{
    Healthy,
    Warning,
    Critical
}

public enum IssueSeverity
{
    Warning,
    Critical
}