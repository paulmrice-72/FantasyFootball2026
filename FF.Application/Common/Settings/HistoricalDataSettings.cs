// FF.Application/Common/Settings/HistoricalDataSettings.cs
namespace FF.Application.Common.Settings;

public class HistoricalDataSettings
{
    public const string SectionName = "HistoricalData";
    public string BasePath { get; init; } = string.Empty;
}