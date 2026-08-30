// FF.Infrastructure/Jobs/SyncRedraftAdpJob.cs
using FF.Application.Interfaces.External;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using Hangfire;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Jobs;

public class SyncRedraftAdpJob(
    IFantasyFootballCalculatorService ffcService,
    MongoDbContext db,
    ILogger<SyncRedraftAdpJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var season = DateTime.UtcNow.Month >= 3
            ? DateTime.UtcNow.Year
            : DateTime.UtcNow.Year - 1;

        logger.LogInformation("SyncRedraftAdpJob starting for season {Season}", season);

        // ── 1. Fetch ADP from FFC ────────────────────────────────────────────
        var adpEntries = await ffcService.GetAdpAsync(season, "ppr", 12, ct);
        if (adpEntries.Count == 0)
        {
            logger.LogWarning("SyncRedraftAdpJob: FFC returned no data — skipping upsert");
            return;
        }

        // ── 2. Build name lookup from dynasty_valuations ─────────────────────
        // Fields are PascalCase: PlayerName, SleeperPlayerId, Position
        var valuationCollection = db.GetCollection<PlayerLookup>("dynasty_valuations");
        var players = await valuationCollection
            .Find(FilterDefinition<PlayerLookup>.Empty)
            .Project(p => new PlayerLookup
            {
                SleeperPlayerId = p.SleeperPlayerId,
                PlayerName = p.PlayerName,
                Position = p.Position,
                NflTeam = p.NflTeam
            })
            .ToListAsync(ct);

        // Deduplicate — multiple seasons per player; one entry is enough for name matching
        var deduped = players
            .Where(p => !string.IsNullOrEmpty(p.PlayerName) && !string.IsNullOrEmpty(p.SleeperPlayerId))
            .GroupBy(p => p.SleeperPlayerId!)
            .Select(g => g.First())
            .ToList();

        var nameLookup = deduped
            .GroupBy(p => NormalizeName(p.PlayerName!))
            .ToDictionary(g => g.Key, g => g.ToList());

        logger.LogInformation("SyncRedraftAdpJob: {Count} unique players in name lookup", deduped.Count);

        // ── 3. Upsert into redraftAdpCache ───────────────────────────────────
        var adpCollection = db.GetCollection<RedraftAdpCacheDocument>("redraftAdpCache");

        int matched = 0, unmatched = 0;

        // Stale-entry cleanup (2026-08-30, FAN-105 follow-up): a player who
        // matched in a prior run but drops out of FFC's feed — or stops
        // matching by name — used to leave a permanent zombie document
        // behind (no upsert ever touches it again, so it just sits at
        // whatever ADP/team it last had, sometimes months stale). Track
        // everyone who matches THIS run and prune anything else for this
        // Season/ScoringFormat afterward, so the cache always reflects only
        // the current FFC pull.
        var matchedSleeperIds = new HashSet<string>();

        foreach (var entry in adpEntries)
        {
            if (entry.Position is "K" or "DST") continue;

            var normalizedName = NormalizeName(entry.Name);

            if (!nameLookup.TryGetValue(normalizedName, out var candidates))
            {
                logger.LogDebug("No match: {Name} ({Position})", entry.Name, entry.Position);
                unmatched++;
                continue;
            }

            var match = candidates.FirstOrDefault(c =>
                string.Equals(c.Position, entry.Position, StringComparison.OrdinalIgnoreCase))
                ?? candidates.First();

            var filter = Builders<RedraftAdpCacheDocument>.Filter.And(
                Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.SleeperPlayerId, match.SleeperPlayerId),
                Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.Season, season),
                Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.ScoringFormat, "ppr"));

            var update = Builders<RedraftAdpCacheDocument>.Update
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .Set(x => x.SleeperPlayerId, match.SleeperPlayerId!)
                .Set(x => x.PlayerName, entry.Name)
                .Set(x => x.Position, entry.Position)
                // FFC's own ADP feed leaves "team" null/empty for a chunk of
                // established veterans (offseason data gap on their end) —
                // fall back to the roster team we already have on file from
                // dynasty_valuations (kept current by PlayerSyncJob) rather
                // than showing those players as free agents.
                .Set(x => x.NflTeam, string.IsNullOrEmpty(entry.Team)
                    ? (string.IsNullOrEmpty(match.NflTeam) ? null : match.NflTeam)
                    : entry.Team)
                .Set(x => x.Adp, entry.Adp)
                .Set(x => x.AdpRound, entry.AdpRound)
                .Set(x => x.Season, season)
                .Set(x => x.ScoringFormat, "ppr")
                .Set(x => x.TeamCount, 12)
                .Set(x => x.SyncedAt, DateTime.UtcNow);

            await adpCollection.UpdateOneAsync(filter, update,
                new UpdateOptions { IsUpsert = true }, ct);

            matched++;
            matchedSleeperIds.Add(match.SleeperPlayerId!);
        }

        // ── 4. Prune stale entries not matched this run ──────────────────────
        var staleFilter = Builders<RedraftAdpCacheDocument>.Filter.And(
            Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.Season, season),
            Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.ScoringFormat, "ppr"),
            Builders<RedraftAdpCacheDocument>.Filter.Nin(x => x.SleeperPlayerId, matchedSleeperIds));

        var pruneResult = await adpCollection.DeleteManyAsync(staleFilter, ct);

        logger.LogInformation(
            "SyncRedraftAdpJob complete: {Matched} matched, {Unmatched} unmatched of {Total}, " +
            "{Pruned} stale entries pruned",
            matched, unmatched, adpEntries.Count, pruneResult.DeletedCount);
    }

    private static string NormalizeName(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var suffix in new[] { " jr.", " jr", " sr.", " sr", " iii", " ii", " iv" })
            if (n.EndsWith(suffix)) n = n[..^suffix.Length];
        n = n.Replace("'", "").Replace(".", "").Replace("-", " ").Trim();
        while (n.Contains("  ")) n = n.Replace("  ", " ");
        return n;
    }

    private class PlayerLookup
    {
        public string? SleeperPlayerId { get; set; }
        public string? PlayerName { get; set; }
        public string? Position { get; set; }
        public string? NflTeam { get; set; }
    }
}