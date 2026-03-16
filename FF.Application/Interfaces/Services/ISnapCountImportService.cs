namespace FF.Application.Interfaces.Services;

public record SnapCountImportResult(
    bool Success,
    int Inserted,
    int Replaced,
    string? ErrorMessage
);

public interface ISnapCountImportService
{
    Task<SnapCountImportResult> ImportAsync(int season, CancellationToken cancellationToken = default);
}