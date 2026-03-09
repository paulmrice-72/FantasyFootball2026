namespace FF.Application.Interfaces.Services.Usage;

public interface IUsageMetricsService
{
    Task AggregatePlayerMetricsAsync(
        string playerId,
        int season,                    // int not string
        CancellationToken ct = default);

    Task AggregateAllPlayersAsync(
        int season,                    // int not string
        CancellationToken ct = default);
}