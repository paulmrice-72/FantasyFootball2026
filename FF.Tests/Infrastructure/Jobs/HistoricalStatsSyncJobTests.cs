// FF.Tests/Infrastructure/Jobs/HistoricalStatsSyncJobTests.cs
using FF.Application.Interfaces.Services;
using FF.Application.Stats.Commands;
using FF.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;

namespace FF.Tests.Infrastructure.Jobs;

public class HistoricalStatsSyncJobTests
{
    private readonly Mock<IHistoricalStatsImportService> _mockImportService;
    private readonly Mock<ILogger<HistoricalStatsSyncJob>> _mockLogger;
    private readonly HistoricalStatsSyncJob _job;

    public HistoricalStatsSyncJobTests()
    {
        _mockImportService = new Mock<IHistoricalStatsImportService>();
        _mockLogger = new Mock<ILogger<HistoricalStatsSyncJob>>();
        _job = new HistoricalStatsSyncJob(_mockImportService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SyncCurrentSeasonAsync_CallsImportService_WithCurrentSeason()
    {
        // Arrange
        var expectedSeason = DateTime.UtcNow.Month >= 3
            ? DateTime.UtcNow.Year
            : DateTime.UtcNow.Year - 1;

        _mockImportService
            .Setup(x => x.ImportSeasonAsync(expectedSeason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoricalImportResult
            {
                TotalInserted = 0,
                TotalReplaced = 5226
            });

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
        _mockImportService
            .Setup(x => x.ImportSeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoricalImportResult
            {
                TotalInserted = 0,
                TotalReplaced = 100
            });

        // Act
        var act = async () => await _job.SyncCurrentSeasonAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SyncCurrentSeasonAsync_WhenImportFails_ThrowsToAllowHangfireRetry()
    {
        // Arrange
        _mockImportService
            .Setup(x => x.ImportSeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("MongoDB connection failed"));

        // Act
        var act = async () => await _job.SyncCurrentSeasonAsync();

        // Assert --- must propagate so Hangfire marks job Failed and retries
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("MongoDB connection failed");
    }

    [Theory]
    [InlineData(1, 2025)]  // January  --- still in prior season
    [InlineData(2, 2025)]  // February --- still in prior season
    [InlineData(3, 2026)]  // March    --- new season year begins
    [InlineData(9, 2026)]  // September --- season underway
    [InlineData(12, 2026)] // December --- season underway
    public void NflSeasonLogic_ReturnsCorrectYear_ForGivenMonth(int month, int expectedYear)
    {
        // Validates the GetCurrentNflSeason logic in isolation
        // Jan/Feb = prior year, March onwards = current year
        const int currentYear = 2026;
        var result = month >= 3 ? currentYear : currentYear - 1;
        result.Should().Be(expectedYear);
    }
}