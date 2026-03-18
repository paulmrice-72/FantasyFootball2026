// FF.Tests/Application/RoleClassificationServiceTests.cs
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FluentAssertions;

namespace FF.Tests.Application;

public class RoleClassificationServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static PlayerUsageMetricsDocument Metrics(
        string position,
        decimal targetShareSeason = 0m,
        decimal woprSeason = 0m,
        decimal aDotSeason = 0m,
        decimal snapPctSeason = 0m,
        decimal carryShareSeason = 0m) =>
        new()
        {
            Position = position,
            TargetShareSeason = targetShareSeason,
            WoprSeason = woprSeason,
            ADotSeason = aDotSeason,
            SnapPctSeason = snapPctSeason,
            CarryShareSeason = carryShareSeason
        };

    // ── WR Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_WR1Alpha_HighTargetShareAndWopr()
    {
        var m = Metrics("WR", targetShareSeason: 0.28m, woprSeason: 0.55m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.WR1Alpha);
    }

    [Fact]
    public void Classify_DeepThreat_HighADotLowTargetShare()
    {
        var m = Metrics("WR", targetShareSeason: 0.12m, aDotSeason: 15.5m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.DeepThreat);
    }

    [Fact]
    public void Classify_SlotPossession_HighSnapLowADot()
    {
        var m = Metrics("WR", snapPctSeason: 0.72m, aDotSeason: 6.2m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.SlotPossession);
    }

    [Fact]
    public void Classify_WR_Unknown_WhenNoThresholdsMet()
    {
        // Low everything — doesn't fit any role
        var m = Metrics("WR", targetShareSeason: 0.08m, woprSeason: 0.15m,
            aDotSeason: 10m, snapPctSeason: 0.40m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.Unknown);
    }

    [Fact]
    public void Classify_WR1Alpha_TakesPriorityOverDeepThreat()
    {
        // Meets both WR1Alpha AND DeepThreat thresholds — WR1Alpha should win (checked first)
        var m = Metrics("WR", targetShareSeason: 0.26m, woprSeason: 0.52m, aDotSeason: 15m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.WR1Alpha);
    }

    // ── RB Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_BellCow_HighCarryShareAndSnap()
    {
        var m = Metrics("RB", snapPctSeason: 0.70m, carryShareSeason: 0.65m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.BellCow);
    }

    [Fact]
    public void Classify_PassCatcher_HighTargetShareLowCarries()
    {
        var m = Metrics("RB", targetShareSeason: 0.10m, carryShareSeason: 3m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.PassCatcher);
    }

    [Fact]
    public void Classify_Handcuff_LowSnapAndCarries()
    {
        var m = Metrics("RB", snapPctSeason: 0.20m, carryShareSeason: 3m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.Handcuff);
    }

    // ── TE Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_SeamReceiver_HighWoprAndADot()
    {
        var m = Metrics("TE", woprSeason: 0.40m, aDotSeason: 10.5m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.SeamReceiver);
    }

    [Fact]
    public void Classify_BlockerSpot_LowSnapAndTargets()
    {
        var m = Metrics("TE", snapPctSeason: 0.35m, targetShareSeason: 0.03m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.BlockerSpot);
    }

    // ── QB Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_StartingQB_HighSnap()
    {
        var m = Metrics("QB", snapPctSeason: 0.95m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.StartingQB);
    }

    [Fact]
    public void Classify_BackupQB_LowSnap()
    {
        var m = Metrics("QB", snapPctSeason: 0.10m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.BackupQB);
    }

    [Fact]
    public void Classify_QB_Unknown_WhenNoSnaps()
    {
        var m = Metrics("QB", snapPctSeason: 0m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.Unknown);
    }

    // ── Edge Cases ────────────────────────────────────────────────────────

    [Fact]
    public void Classify_UnknownPosition_ReturnsUnknown()
    {
        var m = Metrics("K", snapPctSeason: 0.99m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.Unknown);
    }

    [Fact]
    public void Classify_SlotPossession_RequiresPositiveADot()
    {
        // aDOT = 0 means no data — should not classify as slot
        var m = Metrics("WR", snapPctSeason: 0.72m, aDotSeason: 0m);
        RoleClassificationService.Classify(m).Should().Be(PlayerRole.Unknown);
    }
}