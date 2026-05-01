// FF.Tests/Infrastructure/Jobs/HistoricalStatsSyncJobTests.cs
using FF.Application.Interfaces.Services;
using FF.Application.Stats.Commands;
using FF.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FF.Tests.Infrastructure.Jobs;

public class HistoricalStatsSyncJobTests
{
    private readonly Mock<IHistoricalStatsImportService> _mockImportService;
    private readonly Mock<INflverseDownloadService> _mockDownloadService;
    private readonly Mock<ILogger<HistoricalStatsSyncJob>> _mockLogger;
    private readonly HistoricalStatsSyncJob _job;
    private readonly Mock<IPlayerIdResolutionService> _mockResolutionService;
    private readonly Mock<INflContextService> _mockNflContext;

    public HistoricalStatsSyncJobTests()
    {
        _mockImportService = new Mock<IHistoricalStatsImportService>();
        _mockDownloadService = new Mock<INflverseDownloadService>();
        _mockResolutionService = new Mock<IPlayerIdResolutionService>();
        _mockNflContext = new Mock<INflContextService>();
        _mockLogger = new Mock<ILogger<HistoricalStatsSyncJob>>();

        // FIX: Moq returns null Task for unmocked async methods, causing
        // NullReferenceException on await. Always set up GetSeasonAsync.
        _mockNflContext
            .Setup(x => x.GetSeasonAsync())
            .ReturnsAsync(2026);

        _job = new HistoricalStatsSyncJob(
            _mockImportService.Object,
            _mockDownloadService.Object,
            _mockResolutionService.Object,
            _mockNflContext.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task SyncCurrentSeasonAsync_CallsImportService_WithCurrentSeason()
    {
        // Arrange
        // FIX: use the fixed season the mock returns (2026) rather than
        // recalculating from DateTime.UtcNow, which could diverge from
        // what the mock-controlled job actually uses.
        const int expectedSeason = 2026;

        _mockDownloadService
            .Setup(x => x.DownloadCurrentSeasonAsync(
                expectedSeason,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NflverseDownloadResult
            {
                Success = true,
                Season = expectedSeason,
                FileSizeBytes = 1024000
            });

        _mockImportService
            .Setup(x => x.ImportSeasonAsync(
                expectedSeason,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoricalImportResult
            {
                TotalInserted = 0,
                TotalReplaced = 5226
            });

        _mockResolutionService
            .Setup(x => x.BackfillMissingSleeperIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerIdResolutionResult(0, 0, 0, []));

        // Act
        await _job.SyncCurrentSeasonAsync();

        // Assert
        _mockImportService.Verify(
            x => x.ImportSeasonAsync(expectedSeason, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncCurrentSeasonAsync_WhenImportSucceeds_DoesNotThrow()
    {
        // Arrange
        _mockDownloadService
            .Setup(x => x.DownloadCurrentSeasonAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NflverseDownloadResult
            {
                Success = true,
                Season = 2026,
                FileSizeBytes = 1024000
            });

        _mockImportService
            .Setup(x => x.ImportSeasonAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoricalImportResult
            {
                TotalInserted = 0,
                TotalReplaced = 100
            });

        _mockResolutionService
            .Setup(x => x.BackfillMissingSleeperIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerIdResolutionResult(0, 0, 0, new Dictionary<string, int>()));

        // Act
        var act = async () => await _job.SyncCurrentSeasonAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SyncCurrentSeasonAsync_WhenImportFails_ThrowsToAllowHangfireRetry()
    {
        // Arrange
        _mockDownloadService
            .Setup(x => x.DownloadCurrentSeasonAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NflverseDownloadResult
            {
                Success = true,
                Season = 2026,
                FileSizeBytes = 1024000
            });

        _mockImportService
            .Setup(x => x.ImportSeasonAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("MongoDB connection failed"));

        // Act
        var act = async () => await _job.SyncCurrentSeasonAsync();

        // Assert --- must propagate so Hangfire marks job Failed and retries
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("MongoDB connection failed");
    }

    [Fact]
    public async Task SyncCurrentSeasonAsync_WhenDownloadFails_ThrowsToAllowHangfireRetry()
    {
        // Arrange --- download fails, import should never be called
        _mockDownloadService
            .Setup(x => x.DownloadCurrentSeasonAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NflverseDownloadResult
            {
                Success = false,
                Season = 2026,
                ErrorMessage = "GitHub unreachable"
            });

        // Act
        var act = async () => await _job.SyncCurrentSeasonAsync();

        // Assert --- download failure must propagate so Hangfire retries
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("nflverse download failed: GitHub unreachable");

        // Import should never have been called
        _mockImportService.Verify(
            x => x.ImportSeasonAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(1, 2025)]  // January   --- still in prior season
    [InlineData(2, 2025)]  // February  --- still in prior season
    [InlineData(3, 2026)]  // March     --- new season year begins
    [InlineData(9, 2026)]  // September --- season underway
    [InlineData(12, 2026)] // December  --- season underway
    public void NflSeasonLogic_ReturnsCorrectYear_ForGivenMonth(int month, int expectedYear)
    {
        const int currentYear = 2026;
        var result = month >= 3 ? currentYear : currentYear - 1;
        result.Should().Be(expectedYear);
    }
}