namespace FF.Application.Interfaces.Services;

public interface IPlayerIdResolutionService
{
    Task<Dictionary<string, string>> BuildGsisToSleeperMapAsync(
        CancellationToken cancellationToken = default);

    Task<PlayerIdResolutionResult> BackfillMissingSleeperIdsAsync(
        CancellationToken cancellationToken = default);
}

public record PlayerIdResolutionResult(
    int TotalProcessed,
    int Resolved,
    int Unresolved,
    Dictionary<string, int> UnresolvedByPosition);