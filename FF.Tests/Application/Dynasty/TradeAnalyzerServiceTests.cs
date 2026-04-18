using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FF.Tests.Application.Dynasty;

public class TradeAnalyzerServiceTests
{
    private readonly Mock<IDynastyValuationRepository> _valuationRepo = new();
    private readonly Mock<IPickValueRepository> _pickValueRepo = new();
    // ADD THESE:
    private readonly Mock<IRosterPlayerRepository> _rosterPlayerRepo = new();
    private readonly Mock<ILeagueRepository> _leagueRepo = new();
    private TradeAnalyzerService CreateSut() => new(
        _valuationRepo.Object,
        _pickValueRepo.Object,
        _rosterPlayerRepo.Object,
        _leagueRepo.Object,
        NullLogger<TradeAnalyzerService>.Instance);

    private static DynastyValuationDocument MakeValuation(
        string sleeperPlayerId, string name, string pos, int age,
        double tradeValue, double breakoutScore = 50,
        BreakoutClassification classification = BreakoutClassification.OnCurve,
        double yearsOfPrime = 3.0) =>
        new()
        {
            SleeperPlayerId = sleeperPlayerId,
            PlayerName = name,
            Position = pos,
            Age = age,
            TradeValue = tradeValue,
            BreakoutScore = breakoutScore,
            BreakoutClassification = classification,
            YearsOfPrimeRemaining = yearsOfPrime
        };

    // ── Grade computation ─────────────────────────────────────────────────

    [Theory]
    [InlineData(60.0, 80.0, "A")]   // you give 60, get 80 — win
    [InlineData(65.0, 75.0, "B")]   // you give 65, get 75
    [InlineData(65.0, 70.0, "C")]   // roughly even
    [InlineData(70.0, 60.0, "D")]   // you give more
    [InlineData(70.0, 50.0, "F")]   // you give much more
    public async Task AnalyzeAsync_CorrectGrade_ForValueDifferential(
        double myValue, double theirValue, string expectedGrade)
    {
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("my1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("my1", "My Player", "WR", 25, myValue));
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("their1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("their1", "Their Player", "WR", 25, theirValue));

        var sut = CreateSut();
        var result = await sut.AnalyzeAsync("user1", ["my1"], ["their1"], [], [], 2026);

        result.Grade.Should().Be(expectedGrade);
    }

    // ── Value aggregation ─────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_MultiPlayerTrade_SumsValueCorrectly()
    {
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("my1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("my1", "Player A", "WR", 25, 60.0));
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("my2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("my2", "Player B", "RB", 24, 15.0));
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("their1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("their1", "Player C", "QB", 28, 100.0));

        var sut = CreateSut();
        var result = await sut.AnalyzeAsync("user1", ["my1", "my2"], ["their1"], [], [], 2026);

        result.MySideValue.Should().BeApproximately(75.0, 0.01);   // my1=60 + my2=15
        result.TheirSideValue.Should().BeApproximately(100.0, 0.01); // their1=100
        result.ValueDifferential.Should().BeApproximately(-25.0, 0.01); // 75-100
    }

    // ── Unknown player handling ───────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_UnknownPlayer_GetsZeroValue()
    {
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("known", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("known", "Known Player", "WR", 25, 70.0));
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DynastyValuationDocument?)null);

        var sut = CreateSut();
        var result = await sut.AnalyzeAsync("user1", ["unknown"], ["known"], [], [], 2026);

        result.MySideValue.Should().Be(0);       // unknown player = 0
        result.TheirSideValue.Should().BeApproximately(70.0, 0.01);
        result.Grade.Should().Be("A");           // getting 70, giving 0 = A
    }

    // ── Insights ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_YoungForOld_GeneratesAgeInsight()
    {
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("young", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("young", "Young WR", "WR", 22, 65.0));
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("old", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("old", "Old RB", "RB", 30, 60.0));

        var sut = CreateSut();
        var result = await sut.AnalyzeAsync("user1", ["old"], ["young"], [], [], 2026);
        result.KeyInsights.Should().Contain(s => s.Contains("younger"));
    }

    [Fact]
    public async Task AnalyzeAsync_AcquiringBreakoutPlayer_GeneratesBreakoutInsight()
    {
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("breakout", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("breakout", "Hot WR", "WR", 23, 70.0,
                breakoutScore: 85,
                classification: BreakoutClassification.Breakout));
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync("steady", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("steady", "Steady RB", "RB", 26, 65.0));

        var sut = CreateSut();
        var result = await sut.AnalyzeAsync("user1", ["steady"], ["breakout"], [], [], 2026);
        result.KeyInsights.Should().Contain(s => s.Contains("breakout"));
    }

    // ── Document structure ────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ReturnsFullyPopulatedDocument()
    {
        _valuationRepo
            .Setup(r => r.GetBySleeperIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeValuation("p1", "Player", "WR", 25, 70.0));

        var sut = CreateSut();
        var result = await sut.AnalyzeAsync("user1", ["my1"], ["their1"], [], [], 2026);

        result.Id.Should().NotBeNullOrEmpty();
        result.UserId.Should().Be("user1");
        result.Grade.Should().NotBeNullOrEmpty();
        result.Recommendation.Should().NotBeNullOrEmpty();
        result.MySide.Should().HaveCount(1);
        result.TheirSide.Should().HaveCount(1);
    }
}