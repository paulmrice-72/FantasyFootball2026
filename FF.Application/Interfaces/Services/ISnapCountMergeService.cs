namespace FF.Application.Interfaces.Services;

public record SnapCountMergeResult(
    bool Success,
    int Merged,
    int Unmatched,
    string? ErrorMessage
);

public interface ISnapCountMergeService
{
    Task<SnapCountMergeResult> MergeAsync(int season, CancellationToken cancellationToken = default);
}