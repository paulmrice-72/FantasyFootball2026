// FF.Infrastructure/Services/SleeperPlayerSyncService.cs
//
// Fetches the full Sleeper player universe (~5,000+ players) and upserts
// them into the local Players table.
//
// FULL OVERWRITE STRATEGY:
// For existing players, we overwrite all fields. This keeps the DB in
// perfect sync with Sleeper without needing to track which fields changed.
// The tradeoff is slightly more DB writes, but player syncs run weekly
// and the simplicity is worth it.
//
// BATCH WRITE STRATEGY:
// We write in batches of 250 to avoid holding a huge transaction open.
// If the job fails halfway through, previously written batches are
// committed and the next run will overwrite them again cleanly.

using FF.Application.Interfaces.Services;
using FF.Application.Players.Commands.SyncPlayers;
using FF.Domain.Enums;
using FF.Infrastructure.ExternalApis.Sleeper;
using FF.Infrastructure.ExternalApis.Sleeper.Mappers;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class SleeperPlayerSyncService(
    ISleeperApiClient sleeperApi,
    FFDbContext dbContext,
    ILogger<SleeperPlayerSyncService> logger) : ISleeperPlayerSyncService
{
    private readonly ISleeperApiClient _sleeperApi = sleeperApi;
    private readonly FFDbContext _dbContext = dbContext;
    private readonly ILogger<SleeperPlayerSyncService> _logger = logger;

    private const int BatchSize = 250;

    public async Task<SyncPlayersResult> SyncAllPlayersAsync(
        CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;

        // ── Step 1: Fetch all players from Sleeper ────────────────────────
        _logger.LogInformation("Fetching full player universe from Sleeper API");
        var sleeperPlayers = await _sleeperApi.GetAllPlayersAsync(cancellationToken);

        _logger.LogInformation(
            "Received {Count} players from Sleeper", sleeperPlayers.Count);

        // ── Step 2: Load existing player IDs from DB ──────────────────────
        // Load into a dictionary for O(1) lookup instead of hitting DB per player
        var existingPlayers = await _dbContext.Players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionaryAsync(p => p.SleeperPlayerId!, cancellationToken);

        _logger.LogInformation(
            "Found {Count} existing players in local DB", existingPlayers.Count);

        // ── Step 3: Process players in batches ────────────────────────────
        var added = 0;
        var updated = 0;
        var skipped = 0;
        var batchCount = 0;

        var allSleeperEntries = sleeperPlayers.Values.ToList();
        var totalBatches = (int)Math.Ceiling(allSleeperEntries.Count / (double)BatchSize);

        for (var i = 0; i < allSleeperEntries.Count; i += BatchSize)
        {
            batchCount++;
            var batch = allSleeperEntries.Skip(i).Take(BatchSize).ToList();

            _logger.LogDebug(
                "Processing batch {Batch}/{Total} ({Count} players)",
                batchCount, totalBatches, batch.Count);

            foreach (var sleeperPlayer in batch)
            {
                if (string.IsNullOrEmpty(sleeperPlayer.PlayerId))
                {
                    skipped++;
                    continue;
                }

                // Skip non-fantasy positions (LB, CB, OL, etc.)
                var domainPlayer = SleeperPlayerMapper.ToDomainEntity(sleeperPlayer);
                if (domainPlayer is null)
                {
                    skipped++;
                    continue;
                }

                if (existingPlayers.TryGetValue(sleeperPlayer.PlayerId, out var existing))
                {
                    // ── Full overwrite of all fields ──────────────────────
                    existing.UpdateTeam(sleeperPlayer.Team);
                    existing.UpdateStatus(SleeperPlayerMapper.MapStatus(sleeperPlayer));
                    existing.UpdateFields(
                        firstName: sleeperPlayer.FirstName!,
                        lastName: sleeperPlayer.LastName!,
                        position: domainPlayer.Position,
                        age: sleeperPlayer.Age,
                        yearsExperience: sleeperPlayer.YearsExp,
                        jerseyNumber: sleeperPlayer.Number);
                    existing.SetUpdated();
                    updated++;
                }
                else
                {
                    // ── New player — add to context ───────────────────────
                    _dbContext.Players.Add(domainPlayer);
                    existingPlayers[sleeperPlayer.PlayerId] = domainPlayer;
                    added++;
                }
            }

            // Commit each batch independently
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Batch {Batch}/{Total} committed — Running totals: " +
                "+{Added} added, ~{Updated} updated",
                batchCount, totalBatches, added, updated);
        }

        var duration = DateTime.UtcNow - started;

        return new SyncPlayersResult(
            PlayersAdded: added,
            PlayersUpdated: updated,
            PlayersSkipped: skipped,
            TotalProcessed: added + updated + skipped,
            Duration: duration);
    }
}
