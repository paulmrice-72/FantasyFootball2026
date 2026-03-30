namespace FF.Application.Common;

public static class CacheKeys
{
    public static string Projections(int season, int week) =>
        $"projections:{season}:{week}";

    public static string VorpRecommendations(string leagueId, int season, int week, string? position, int top) =>
        $"vorp:{leagueId}:{season}:{week}:{position ?? "ALL"}:{top}";

    public static string EmergenceAlerts(int season, string? position) =>
        $"emergence:{season}:{position ?? "ALL"}";

    public static string DynastyRankings(string position) =>
        $"dynasty:{position}";
}