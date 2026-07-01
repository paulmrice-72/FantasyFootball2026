// FF.Application/Interfaces/Services/IEspnStatsSyncService.cs
namespace FF.Application.Interfaces.Services;

public interface IEspnStatsSyncService
{
    /// <summary>
    /// Phase 1: Downloads nflverse players.csv and populates EspnId on the
    /// Players table for all rows with a matching GsisId.
    /// Run once; re-run whenever new players are added.
    /// </summary>
    Task<EspnIdSyncResult> SyncEspnIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Phase 2: For every Player with an EspnId, fetches 2025 season stats
    /// from the ESPN API and upserts a season-average SimulationResultDocument
    /// (Week=0) into the simulation_results collection.
    /// This directly seeds CareerSimulationService with real 2025 FPPG data.
    /// </summary>
    Task<EspnStatsSyncResult> SyncStatsAsync(int season, CancellationToken ct = default);
}

public record EspnIdSyncResult(
    int PlayersMatched,
    int PlayersSkipped,
    int PlayersAlreadyHadId,
    TimeSpan Duration);

public record EspnStatsSyncResult(
    int PlayersProcessed,
    int PlayersUpserted,
    int PlayersFailed,
    int PlayersSkippedNoEspnId,
    TimeSpan Duration);