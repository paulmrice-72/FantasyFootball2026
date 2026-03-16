using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Infrastructure.ExternalApis.CsvImport.Parsers;
using FF.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FF.Tests.SnapCounts;

public class SnapCountImportServiceTests
{
    private readonly INflverseDownloadService _downloadService;
    private readonly ISnapCountRepository _snapCountRepository;
    private readonly SnapCountCsvParser _parser;
    private readonly SnapCountImportService _service;

    public SnapCountImportServiceTests()
    {
        _downloadService = Substitute.For<INflverseDownloadService>();
        _snapCountRepository = Substitute.For<ISnapCountRepository>();
        _parser = new SnapCountCsvParser();
        _service = new SnapCountImportService(
            _downloadService,
            _snapCountRepository,
            _parser,
            NullLogger<SnapCountImportService>.Instance);
    }

    [Fact]
    public async Task ImportAsync_DownloadFails_ReturnsFailure()
    {
        _downloadService.DownloadSnapCountsAsync(2024, Arg.Any<CancellationToken>())
            .Returns(new NflverseDownloadResult
            {
                Success = false,
                ErrorMessage = "Network error"
            });

        var result = await _service.ImportAsync(2024);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Download failed");
    }

    [Fact]
    public async Task ImportAsync_ValidFile_ReturnsSuccess()
    {
        var csvPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(csvPath,
            "player,team,week,pos,offense_snaps,offense_pct\n" +
            "Justin Jefferson,MIN,1,WR,65,0.95\n" +
            "Davante Adams,LV,1,WR,60,0.88\n");

        _downloadService.DownloadSnapCountsAsync(2024, Arg.Any<CancellationToken>())
            .Returns(new NflverseDownloadResult
            {
                Success = true,
                SavedPath = csvPath,
                Season = 2024
            });

        _snapCountRepository.UpsertBatchAsync(Arg.Any<IEnumerable<FF.Domain.Documents.SnapCountDocument>>(),
            Arg.Any<CancellationToken>())
            .Returns((2, 0));

        var result = await _service.ImportAsync(2024);

        result.Success.Should().BeTrue();
        result.Inserted.Should().Be(2);
        result.Replaced.Should().Be(0);

        File.Delete(csvPath);
    }
}