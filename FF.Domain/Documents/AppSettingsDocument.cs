namespace FF.Domain.Documents;

/// <summary>
/// Singleton admin settings document stored in MongoDB app_settings collection.
/// Only one document ever exists — Id is always "global".
/// </summary>
public class AppSettingsDocument
{
    public string Id { get; set; } = "global";

    /// <summary>When set, all jobs and queries use this season instead of calendar.</summary>
    public int? SimulationSeasonOverride { get; set; }

    /// <summary>When set, all jobs and queries use this week instead of calendar.</summary>
    public int? SimulationWeekOverride { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = "system";
}