namespace FF.Application.Interfaces.Services.Usage;

public interface IUsageMetricsService
{
    Task AggregatePlayerMetricsAsync(
        string playerId,
        int season,                    // int not string
        CancellationToken ct = default);

    /// <summary>
    /// Aggregates usage metrics for every player with game logs in the given season.
    /// Returns the number of players found — 0 means there were no game logs for that
    /// season, which is a no-op the caller must be able to see rather than infer.
    /// </summary>
    Task<int> AggregateAllPlayersAsync(
        int season,                    // int not string
        CancellationToken ct = default);
}
