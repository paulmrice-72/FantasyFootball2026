// FF.Application/Stats/Queries/GetDataQuality/DataQualityRules.cs
//
// Centralised rule definitions for data quality validation.
// Expected document counts are based on verified import results:
//   2022: 5,250 | 2023: 5,293 | 2024: 5,226

namespace FF.Application.Stats.Queries.GetDataQuality;

public static class DataQualityRules
{
    // Expected document count ranges per season
    // Allow 5% variance either side of known good counts
    public static readonly Dictionary<int, (long Min, long Max)> ExpectedSeasonCounts = new()
    {
        { 2022, (4987, 5512) },   // 5,250 ± 5%
        { 2023, (5028, 5557) },   // 5,293 ± 5%
        { 2024, (4964, 5487) },   // 5,226 ± 5%
    };

    // Stat range checks — values outside these are flagged
    public static readonly (decimal Min, decimal Max) PassingYardsRange = (-10, 600);
    public static readonly (decimal Min, decimal Max) RushingYardsRange = (-20, 300);
    public static readonly (decimal Min, decimal Max) ReceivingYardsRange = (-10, 300);
    public static readonly (int Min, int Max) TargetsRange = (0, 25);
    public static readonly (decimal Min, decimal Max) FantasyPointsRange = (-5, 60);
    public static readonly (decimal Min, decimal Max) TargetShareRange = (0, 1);

    // Valid positions
    public static readonly string[] ValidPositions = ["QB", "RB", "WR", "TE", "K"];

    // Minimum expected positions per season per week
    // A healthy week should have all skill positions represented
    public static readonly string[] RequiredPositions = ["QB", "RB", "WR", "TE"];

    // Rule name constants — used in DataQualityIssue.Rule
    public const string RuleSeasonCount = "SEASON_COUNT";
    public const string RuleStatRange = "STAT_RANGE";
    public const string RuleMissingPlayerId = "MISSING_PLAYER_ID";
    public const string RuleInvalidPosition = "INVALID_POSITION";
    public const string RuleMissingFantasyPoints = "MISSING_FANTASY_POINTS";
    public const string RuleTargetShareRange = "TARGET_SHARE_RANGE";
}