using CsvHelper.Configuration.Attributes;

namespace FF.Infrastructure.ExternalApis.Nflverse.Dtos;

public class NflverseRosterRow
{
    [Name("gsis_id")]
    public string? GsisId { get; set; }

    [Name("sleeper_id")]
    public string? SleeperId { get; set; }

    [Name("full_name")]
    public string? FullName { get; set; }

    [Name("position")]
    public string? Position { get; set; }

    [Name("team")]
    public string? Team { get; set; }

    [Name("season")]
    public int? Season { get; set; }
}