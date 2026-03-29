// FF.Tests/Application/Emergence/EmergenceDetectionServiceTests.cs
using FF.Application.Features.EmergenceAlert.Commands;
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using FF.Domain.Documents;
using FluentAssertions;
using Moq;

namespace FF.Tests.Application.Emergence;

public class EmergenceDetectionServiceTests
{
    private readonly Mock<IPlayerUsageMetricsRepository> _metricsRepo = new();
    private readonly Mock<IEmergenceAlertRepository> _alertRepo = new();

    private EmergenceDetectionService CreateSut() =>
        new(_metricsRepo.Object, _alertRepo.Object);

    // ── No alerts when metrics are flat ──────────────────────────────────

    [Fact]
    public async Task Handle_NoAlerts_WhenMetricsBelowAllThresholds()
    {
        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = "p1",
            PlayerName = "Test Player",
            Position = "WR",
            NflTeam = "KC",
            Season = 2026,
            SnapPct3Wk = 0.50m,
            SnapPctSeason = 0.48m,   // delta 0.02 — below 0.18 threshold
            TargetShare3Wk = 0.18m,
            TargetShareSeason = 0.15m,  // delta 0.03 — below 0.08 threshold
            Wopr3Wk = 0.30m,
            WoprSeason = 0.28m    // delta 0.02 — below 0.12 threshold
        };

        _metricsRepo
            .Setup(r => r.GetBySeasonAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([metrics]);

        var result = await CreateSut().Handle(
            new DetectEmergenceCommand(2026, 5), CancellationToken.None);

        result.PlayersScanned.Should().Be(1);
        result.AlertsGenerated.Should().Be(0);

        _alertRepo.Verify(
            r => r.UpsertBatchAsync(It.IsAny<IEnumerable<EmergenceAlertDocument>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Snap share surge triggers alert ──────────────────────────────────

    [Fact]
    public async Task Handle_GeneratesSnapSurgeAlert_WhenDeltaExceedsThreshold()
    {
        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = "p1",
            PlayerName = "Snap Surge Player",
            Position = "RB",
            NflTeam = "SF",
            Season = 2026,
            SnapPct3Wk = 0.75m,
            SnapPctSeason = 0.50m,   // delta 0.25 — above 0.18 threshold
            CarryShare3Wk = 0.40m,
            CarryShareSeason = 0.30m // delta 0.10 — below 0.15 threshold
        };

        _metricsRepo
            .Setup(r => r.GetBySeasonAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([metrics]);

        List<EmergenceAlertDocument>? capturedAlerts = null;
        _alertRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<EmergenceAlertDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EmergenceAlertDocument>, CancellationToken>(
                (alerts, _) => capturedAlerts = [.. alerts])
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(
            new DetectEmergenceCommand(2026, 5), CancellationToken.None);

        result.AlertsGenerated.Should().Be(1);
        capturedAlerts.Should().NotBeNull();
        capturedAlerts!.Should().ContainSingle(a =>
            a.TriggerSignal == EmergenceTriggerSignal.SnapShareSurge);

        var alert = capturedAlerts.Single();
        alert.PlayerId.Should().Be("p1");
        alert.PlayerName.Should().Be("Snap Surge Player");
        alert.Position.Should().Be("RB");
        alert.Week.Should().Be(5);
        alert.Season.Should().Be(2026);
        alert.Delta.Should().BeApproximately(0.25m, 0.001m);
        alert.IsAcknowledged.Should().BeFalse();
    }

    // ── Target share surge triggers alert ────────────────────────────────

    [Fact]
    public async Task Handle_GeneratesTargetSurgeAlert_WhenDeltaExceedsThreshold()
    {
        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = "p2",
            PlayerName = "Target Hog",
            Position = "WR",
            NflTeam = "DAL",
            Season = 2026,
            SnapPct3Wk = 0.60m,
            SnapPctSeason = 0.58m,   // below snap threshold
            TargetShare3Wk = 0.28m,
            TargetShareSeason = 0.15m,  // delta 0.13 — above 0.08 threshold
            Wopr3Wk = 0.30m,
            WoprSeason = 0.29m    // below WOPR threshold
        };

        _metricsRepo
            .Setup(r => r.GetBySeasonAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([metrics]);

        List<EmergenceAlertDocument>? capturedAlerts = null;
        _alertRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<EmergenceAlertDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EmergenceAlertDocument>, CancellationToken>(
                (alerts, _) => capturedAlerts = [.. alerts])
            .Returns(Task.CompletedTask);

        await CreateSut().Handle(
            new DetectEmergenceCommand(2026, 5), CancellationToken.None);

        capturedAlerts.Should().ContainSingle(a =>
            a.TriggerSignal == EmergenceTriggerSignal.TargetShareSurge);
    }

    // ── WOPR spike only fires for WR/TE, not RB ──────────────────────────

    [Theory]
    [InlineData("WR", true)]
    [InlineData("TE", true)]
    [InlineData("RB", false)]
    public async Task Handle_WoprSpike_OnlyFiresForWrAndTe(string position, bool expectAlert)
    {
        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = "p3",
            PlayerName = "WOPR Test",
            Position = position,
            NflTeam = "BUF",
            Season = 2026,
            SnapPct3Wk = 0.50m,
            SnapPctSeason = 0.49m,   // below snap threshold
            TargetShare3Wk = 0.20m,
            TargetShareSeason = 0.19m,  // below target threshold
            Wopr3Wk = 0.50m,
            WoprSeason = 0.30m,   // delta 0.20 — above 0.12 threshold
            CarryShare3Wk = 0.10m,
            CarryShareSeason = 0.09m    // below carry threshold
        };

        _metricsRepo
            .Setup(r => r.GetBySeasonAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([metrics]);

        List<EmergenceAlertDocument> capturedAlerts = [];
        _alertRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<EmergenceAlertDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EmergenceAlertDocument>, CancellationToken>(
                (alerts, _) => capturedAlerts = [.. alerts])
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(
            new DetectEmergenceCommand(2026, 5), CancellationToken.None);

        if (expectAlert)
            capturedAlerts.Should().Contain(a =>
                a.TriggerSignal == EmergenceTriggerSignal.WoprSpike);
        else
            capturedAlerts.Should().NotContain(a =>
                a.TriggerSignal == EmergenceTriggerSignal.WoprSpike);
    }

    // ── Carry surge only fires for RB ────────────────────────────────────

    [Theory]
    [InlineData("RB", true)]
    [InlineData("WR", false)]
    [InlineData("TE", false)]
    public async Task Handle_CarrySurge_OnlyFiresForRb(string position, bool expectAlert)
    {
        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = "p4",
            PlayerName = "Carry Test",
            Position = position,
            NflTeam = "PHI",
            Season = 2026,
            SnapPct3Wk = 0.50m,
            SnapPctSeason = 0.49m,
            TargetShare3Wk = 0.15m,
            TargetShareSeason = 0.14m,
            Wopr3Wk = 0.20m,
            WoprSeason = 0.19m,
            CarryShare3Wk = 0.70m,
            CarryShareSeason = 0.40m    // delta 0.30 — above 0.15 threshold
        };

        _metricsRepo
            .Setup(r => r.GetBySeasonAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([metrics]);

        List<EmergenceAlertDocument> capturedAlerts = [];
        _alertRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<EmergenceAlertDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EmergenceAlertDocument>, CancellationToken>(
                (alerts, _) => capturedAlerts = [.. alerts])
            .Returns(Task.CompletedTask);

        await CreateSut().Handle(
            new DetectEmergenceCommand(2026, 5), CancellationToken.None);

        if (expectAlert)
            capturedAlerts.Should().Contain(a =>
                a.TriggerSignal == EmergenceTriggerSignal.CarryShareSurge);
        else
            capturedAlerts.Should().NotContain(a =>
                a.TriggerSignal == EmergenceTriggerSignal.CarryShareSurge);
    }

    // ── Non-skill positions are skipped ──────────────────────────────────

    [Fact]
    public async Task Handle_SkipsNonSkillPositions()
    {
        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = "p5",
            PlayerName = "Kicker",
            Position = "K",
            Season = 2026,
            SnapPct3Wk = 0.99m,
            SnapPctSeason = 0.10m   // huge delta — but K is not a skill position
        };

        _metricsRepo
            .Setup(r => r.GetBySeasonAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([metrics]);

        var result = await CreateSut().Handle(
            new DetectEmergenceCommand(2026, 5), CancellationToken.None);

        result.PlayersScanned.Should().Be(0);
        result.AlertsGenerated.Should().Be(0);
        _alertRepo.Verify(
            r => r.UpsertBatchAsync(It.IsAny<IEnumerable<EmergenceAlertDocument>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Multiple signals on one player generate multiple alerts ──────────

    [Fact]
    public async Task Handle_MultipleSignals_GenerateMultipleAlertsForSamePlayer()
    {
        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = "p6",
            PlayerName = "Breakout WR",
            Position = "WR",
            NflTeam = "CIN",
            Season = 2026,
            SnapPct3Wk = 0.80m,
            SnapPctSeason = 0.50m,   // delta 0.30 — snap surge
            TargetShare3Wk = 0.30m,
            TargetShareSeason = 0.15m,  // delta 0.15 — target surge
            Wopr3Wk = 0.55m,
            WoprSeason = 0.30m    // delta 0.25 — WOPR spike
        };

        _metricsRepo
            .Setup(r => r.GetBySeasonAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([metrics]);

        List<EmergenceAlertDocument> capturedAlerts = [];
        _alertRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<EmergenceAlertDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EmergenceAlertDocument>, CancellationToken>(
                (alerts, _) => capturedAlerts = [.. alerts])
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(
            new DetectEmergenceCommand(2026, 5), CancellationToken.None);

        result.AlertsGenerated.Should().Be(3);
        capturedAlerts.Should().HaveCount(3);
        capturedAlerts.Select(a => a.TriggerSignal).Should().BeEquivalentTo([
            EmergenceTriggerSignal.SnapShareSurge,
            EmergenceTriggerSignal.TargetShareSurge,
            EmergenceTriggerSignal.WoprSpike
        ]);
        capturedAlerts.Should().AllSatisfy(a => a.PlayerId.Should().Be("p6"));
    }

    // ── Empty metrics returns zero results ───────────────────────────────

    [Fact]
    public async Task Handle_EmptyMetrics_ReturnsZeroResults()
    {
        _metricsRepo
            .Setup(r => r.GetBySeasonAsync(2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().Handle(
            new DetectEmergenceCommand(2026, 5), CancellationToken.None);

        result.PlayersScanned.Should().Be(0);
        result.AlertsGenerated.Should().Be(0);
    }
}