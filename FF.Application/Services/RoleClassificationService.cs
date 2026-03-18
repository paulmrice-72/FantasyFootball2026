// FF.Application/Services/RoleClassificationService.cs
using FF.Domain.Documents;
using FF.Domain.Enums;

namespace FF.Application.Services;

/// <summary>
/// Classifies a player into a role archetype based on their usage metrics.
/// Rule-based — no ML required. Thresholds derived from PBI-DIFF-001 addendum.
/// </summary>
public static class RoleClassificationService
{
    public static PlayerRole Classify(PlayerUsageMetricsDocument metrics)
    {
        return metrics.Position switch
        {
            "WR" => ClassifyWr(metrics),
            "RB" => ClassifyRb(metrics),
            "TE" => ClassifyTe(metrics),
            "QB" => ClassifyQb(metrics),
            _ => PlayerRole.Unknown
        };
    }

    private static PlayerRole ClassifyWr(PlayerUsageMetricsDocument m)
    {
        // WR1 Alpha: high volume, high WOPR
        if (m.TargetShareSeason > 0.25m && m.WoprSeason > 0.50m)
            return PlayerRole.WR1Alpha;

        // Deep Threat: high aDOT, low target share
        if (m.ADotSeason > 14m && m.TargetShareSeason < 0.15m)
            return PlayerRole.DeepThreat;

        // Slot Possession: high snap%, short targets
        if (m.SnapPctSeason > 0.60m && m.ADotSeason < 8m && m.ADotSeason > 0m)
            return PlayerRole.SlotPossession;

        return PlayerRole.Unknown;
    }

    private static PlayerRole ClassifyRb(PlayerUsageMetricsDocument m)
    {
        // Bell Cow: dominant carry share and snap volume
        if (m.CarryShareSeason > 0.60m && m.SnapPctSeason > 0.65m)
            return PlayerRole.BellCow;

        // Pass Catcher: target-heavy, low carry share
        // CarryShareSeason currently stores raw carries as proxy — use TargetShare instead
        if (m.TargetShareSeason > 0.08m && m.CarryShareSeason < 5m)
            return PlayerRole.PassCatcher;

        // Handcuff: low snap%, low volume
        if (m.SnapPctSeason < 0.30m && m.CarryShareSeason < 5m)
            return PlayerRole.Handcuff;

        return PlayerRole.Unknown;
    }

    private static PlayerRole ClassifyTe(PlayerUsageMetricsDocument m)
    {
        // Seam Receiver: WR-like usage for a TE
        if (m.WoprSeason > 0.35m && m.ADotSeason > 9m)
            return PlayerRole.SeamReceiver;

        // Blocker/Spot: minimal receiving role
        if (m.SnapPctSeason < 0.50m && m.TargetShareSeason < 0.05m)
            return PlayerRole.BlockerSpot;

        return PlayerRole.Unknown;
    }

    private static PlayerRole ClassifyQb(PlayerUsageMetricsDocument m)
    {
        // Use SnapPct as proxy for starter vs backup
        if (m.SnapPctSeason >= 0.50m)
            return PlayerRole.StartingQB;

        if (m.SnapPctSeason > 0m)
            return PlayerRole.BackupQB;

        return PlayerRole.Unknown;
    }
}