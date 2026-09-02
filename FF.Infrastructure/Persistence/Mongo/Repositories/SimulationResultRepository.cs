// FF.Infrastructure/Persistence/Mongo/Repositories/SimulationResultRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class SimulationResultRepository(
    MongoDbContext database,
    ILogger<SimulationResultRepository> logger) : ISimulationResultRepository
{
    private readonly IMongoCollection<SimulationResultDocument> _collection =
        database.GetCollection<SimulationResultDocument>("simulation_results");

    // ── Week selection policy (PROJ-006 / FAN-121) ────────────────────────
    //
    // Week 0 is the season-average sentinel. The previous rule sorted it as
    // int.MaxValue, so it beat every real week in its season, unconditionally.
    //
    // That was harmless while the only Week 0 rows were true averages of a
    // COMPLETED season. It stopped being harmless on 2026-09-01, when the
    // projection engine began writing a Season 2026 Week 0 row whose contents are
    // a 2025 carryover: that stale preseason snapshot would have outranked every
    // real 2026 week for the whole season, with live results sitting unused in the
    // collection beside it.
    //
    // The rule is now: prefer the highest REAL week; fall back to Week 0 only when
    // the player has no real week in that season. Preseason still resolves to the
    // Week 0 carryover, because that is all that exists.
    private static SimulationResultDocument SelectBest(
        IEnumerable<SimulationResultDocument> playerDocs)
    {
        var docs = playerDocs as ICollection<SimulationResultDocument> ?? playerDocs.ToList();

        var latestRealWeek = docs
            .Where(d => d.Week > 0)
            .OrderByDescending(d => d.Week)
            .FirstOrDefault();

        return latestRealWeek ?? docs.OrderByDescending(d => d.Week).First();
    }

    public async Task UpsertAsync(
        SimulationResultDocument document,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

        var filter = Builders<SimulationResultDocument>.Filter.And(
            Builders<SimulationResultDocument>.Filter.Eq(x => x.PlayerId, document.PlayerId),
            Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, document.Season),
            Builders<SimulationResultDocument>.Filter.Eq(x => x.Week, document.Week));

        var existing = await _collection.Find(filter).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            await _collection.InsertOneAsync(document, cancellationToken: ct);
        }
        else
        {
            var update = Builders<SimulationResultDocument>.Update
                .Set(x => x.SleeperPlayerId, document.SleeperPlayerId)
                .Set(x => x.PlayerName, document.PlayerName)
                .Set(x => x.Position, document.Position)
                .Set(x => x.NflTeam, document.NflTeam)
                .Set(x => x.OpponentTeam, document.OpponentTeam)
                .Set(x => x.Iterations, document.Iterations)
                .Set(x => x.BaseProjection, document.BaseProjection)
                .Set(x => x.StandardDeviation, document.StandardDeviation)
                .Set(x => x.Floor, document.Floor)
                .Set(x => x.Median, document.Median)
                .Set(x => x.Ceiling, document.Ceiling)
                .Set(x => x.Mean, document.Mean)
                .Set(x => x.BoomProbability, document.BoomProbability)
                .Set(x => x.BustProbability, document.BustProbability)
                .Set(x => x.PlayerRole, document.PlayerRole)
                .Set(x => x.ScoringFormat, document.ScoringFormat)
                .Set(x => x.CalculatedAt, document.CalculatedAt)
                .Set(x => x.Spread, document.Spread)
                .Set(x => x.GameScript, document.GameScript);

            await _collection.UpdateOneAsync(
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Id, existing.Id),
                update,
                cancellationToken: CancellationToken.None);
        }
    }

    public async Task UpsertBatchAsync(
        IEnumerable<SimulationResultDocument> documents,
        CancellationToken ct = default)
    {
        var docs = documents.ToList();

        var seasonAvgDocs = docs.Where(d => d.Week == 0).ToList();
        var weeklyDocs = docs.Where(d => d.Week != 0).ToList();

        if (seasonAvgDocs.Count > 0)
            await UpsertSeasonAverageBatchAsync(seasonAvgDocs, ct);

        foreach (var doc in weeklyDocs)
            await UpsertAsync(doc, ct);

        logger.LogInformation(
            "SimulationResultRepository upserted {Count} documents ({SeasonAvg} season-avg, {Weekly} weekly)",
            docs.Count, seasonAvgDocs.Count, weeklyDocs.Count);
    }

    private async Task UpsertSeasonAverageBatchAsync(
        List<SimulationResultDocument> docs, CancellationToken ct)
    {
        var bulkOps = new List<WriteModel<SimulationResultDocument>>();

        foreach (var doc in docs)
        {
            if (string.IsNullOrEmpty(doc.Id))
                doc.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

            var filter = Builders<SimulationResultDocument>.Filter.And(
                Builders<SimulationResultDocument>.Filter.Eq(x => x.SleeperPlayerId, doc.SleeperPlayerId),
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, doc.Season),
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Week, 0));

            var update = Builders<SimulationResultDocument>.Update
                .SetOnInsert(x => x.Id, doc.Id)
                .Set(x => x.PlayerName, doc.PlayerName)
                .Set(x => x.Position, doc.Position)
                .Set(x => x.NflTeam, doc.NflTeam)
                .Set(x => x.Median, doc.Median)
                .Set(x => x.Floor, doc.Floor)
                .Set(x => x.Ceiling, doc.Ceiling)
                .Set(x => x.Mean, doc.Mean)
                .Set(x => x.BaseProjection, doc.BaseProjection)
                .Set(x => x.StandardDeviation, doc.StandardDeviation)
                .Set(x => x.BoomProbability, doc.BoomProbability)
                .Set(x => x.BustProbability, doc.BustProbability)
                .Set(x => x.ScoringFormat, doc.ScoringFormat)
                .Set(x => x.PlayerRole, doc.PlayerRole)
                .Set(x => x.CalculatedAt, doc.CalculatedAt)
                .Set(x => x.Iterations, doc.Iterations)
                .Set(x => x.Spread, doc.Spread)
                .Set(x => x.GameScript, doc.GameScript)
                .Set(x => x.OpponentTeam, doc.OpponentTeam);

            bulkOps.Add(new UpdateOneModel<SimulationResultDocument>(filter, update)
            {
                IsUpsert = true
            });
        }

        if (bulkOps.Count > 0)
            await _collection.BulkWriteAsync(bulkOps, cancellationToken: ct);
    }

    public async Task<SimulationResultDocument?> GetByPlayerAsync(
        string playerId, int season, int week, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.PlayerId == playerId && x.Season == season && x.Week == week)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SimulationResultDocument>> GetByWeekAsync(
        int season, int week, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Season == season && x.Week == week)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Sorted in memory, deliberately — do NOT restore <c>SortByDescending</c> here.
    ///
    /// The driver's default decimal serializer persists <c>decimal</c> as a BSON
    /// STRING, so a server-side sort on Median is LEXICOGRAPHIC: "9.93" ranks above
    /// "19.44" because '9' &gt; '1'. Verified against dev on 2026-09-01 — the top of
    /// the WR list was every player whose median happened to begin with a 9, while
    /// Nacua, St. Brown and Chase sat below them. Every consumer of this method has
    /// been showing a wrong order.
    ///
    /// Materialising first and ordering on the deserialised decimal is correct and
    /// cheap: one position for one week is a small result set. Revert to a
    /// server-side sort only once the fields are actually stored as Decimal128 —
    /// see FAN-127.
    /// </summary>
    public async Task<IReadOnlyList<SimulationResultDocument>> GetByPositionAsync(
        int season, int week, string position, CancellationToken ct = default)
    {
        var docs = await _collection
            .Find(x => x.Season == season && x.Week == week && x.Position == position)
            .ToListAsync(ct);

        return docs.OrderByDescending(d => d.Median).ToList();
    }

    public async Task<SimulationResultDocument?> GetMostRecentBySleeperIdAsync(
        string sleeperPlayerId, int season, CancellationToken ct = default)
    {
        var filter = Builders<SimulationResultDocument>.Filter.And(
            Builders<SimulationResultDocument>.Filter.Eq(x => x.SleeperPlayerId, sleeperPlayerId),
            Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, season));

        var docs = await _collection.Find(filter).ToListAsync(ct);

        return docs.Count == 0 ? null : SelectBest(docs);
    }

    /// <summary>
    /// Latest simulation per player for the requested season, falling back to the
    /// prior season PER PLAYER when that player has nothing for the requested one.
    ///
    /// The old implementation checked <c>results.Count == 0</c> — a batch-level test.
    /// Once ANY player in the request had current-season data the fallback stopped
    /// firing for everyone, so every player without a row silently vanished from the
    /// result and rendered as zero downstream. That was masked for as long as the
    /// current season was completely empty; it surfaced the moment the projection
    /// engine started writing 2026 rows, and it hit exactly the players most likely
    /// to be misvalued — rookies and anyone with a short prior season.
    ///
    /// Returned documents carry their own <c>Season</c> and <c>Week</c>, so callers
    /// can tell a current-season number from a carried-forward one and surface that
    /// in the UI rather than presenting stale data as live.
    /// </summary>
    public async Task<IReadOnlyList<SimulationResultDocument>> GetLatestBySleeperIdsAsync(
        IEnumerable<string> sleeperPlayerIds, int season, CancellationToken ct = default)
    {
        var ids = sleeperPlayerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (ids.Count == 0) return [];

        var filter = Builders<SimulationResultDocument>.Filter.And(
            Builders<SimulationResultDocument>.Filter.In(x => x.SleeperPlayerId, ids),
            Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, season));

        var docs = await _collection.Find(filter).ToListAsync(ct);

        var resolved = docs
            .Where(d => !string.IsNullOrEmpty(d.SleeperPlayerId))
            .GroupBy(d => d.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => SelectBest(g));

        // Only the ids that found nothing fall back — one player at a time.
        var missing = ids.Where(id => !resolved.ContainsKey(id)).ToList();

        var currentSeasonCount = resolved.Count;
        var canFallBack = season >= DateTime.UtcNow.Year && season > 2020;

        if (missing.Count > 0 && canFallBack)
        {
            var fallbackFilter = Builders<SimulationResultDocument>.Filter.And(
                Builders<SimulationResultDocument>.Filter.In(x => x.SleeperPlayerId, missing),
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, season - 1));

            var fallbackDocs = await _collection.Find(fallbackFilter).ToListAsync(ct);

            var recovered = fallbackDocs
                .Where(d => !string.IsNullOrEmpty(d.SleeperPlayerId))
                .GroupBy(d => d.SleeperPlayerId!)
                .ToDictionary(g => g.Key, g => SelectBest(g));

            foreach (var kvp in recovered)
                resolved[kvp.Key] = kvp.Value;

            logger.LogInformation(
                "Sim lookup for season {Season}: {Current} resolved from the requested season, " +
                "{Recovered} of {Missing} recovered from {FallbackSeason}, {Unresolved} still have no data",
                season, currentSeasonCount, recovered.Count, missing.Count,
                season - 1, missing.Count - recovered.Count);
        }
        else if (missing.Count > 0)
        {
            logger.LogInformation(
                "Sim lookup for season {Season}: {Missing} of {Total} players have no data " +
                "and no fallback applies",
                season, missing.Count, ids.Count);
        }

        return resolved.Values.ToList();
    }

    /// <summary>
    /// Fallback lookup by player name + position when the SleeperPlayerId → GSIS
    /// bridge is missing (e.g. rookies whose GSIS ids aren't yet in Sleeper).
    ///
    /// CAUTION: <c>PlayerName</c> is stored abbreviated ("B.Robinson", "T.Etienne"),
    /// so a name can refer to more than one real player. Position does NOT make it
    /// safe, and neither would team: Bijan and Brian Robinson are both RBs and can
    /// sit on the same roster. There is no attribute combination that reliably
    /// separates two people who share an abbreviated name.
    ///
    /// So this does not try to pick the right one. If the name resolves to more
    /// than one distinct <c>PlayerId</c> in a season, it REFUSES — returning no
    /// data is correct, returning another player's projection is not. See FAN-122.
    /// </summary>
    public async Task<SimulationResultDocument?> GetMostRecentByNameAsync(
       string playerName, string position, int season, CancellationToken ct = default)
    {
        for (int s = season; s >= season - 2; s--)
        {
            var filter = Builders<SimulationResultDocument>.Filter.And(
                Builders<SimulationResultDocument>.Filter.Eq(x => x.PlayerName, playerName),
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Position, position),
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, s));

            var results = await _collection
                .Find(filter)
                .ToListAsync(ct);

            if (results.Count == 0) continue;

            // Ambiguity check. Multiple weeks for one player share a PlayerId, so
            // more than one distinct PlayerId here means the name genuinely maps
            // to more than one human. Bail out rather than pick.
            var distinctPlayers = results
                .Select(r => r.PlayerId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            if (distinctPlayers.Count > 1)
            {
                logger.LogWarning(
                    "Name fallback for {Player} ({Pos}) in season {Season} is ambiguous — " +
                    "{Count} distinct players share that abbreviated name ({Ids}). " +
                    "Returning no data rather than guessing. Fix the Sleeper→GSIS mapping " +
                    "for this player (FAN-122).",
                    playerName, position, s, distinctPlayers.Count,
                    string.Join(", ", distinctPlayers));

                return null;
            }

            // Same policy as everywhere else: newest real week wins, Week 0 only
            // when nothing real exists for that season.
            var best = SelectBest(results);

            if (best.Median > 0)
            {
                logger.LogDebug(
                    "Name fallback matched {Player} ({Pos}) via season {Season} week {Week}",
                    playerName, position, s, best.Week);
                return best;
            }
        }
        return null;
    }

    public async Task<List<SimulationResultDocument>> GetAllSeasonAveragesAsync(
        CancellationToken ct = default)
    {
        var filter = Builders<SimulationResultDocument>.Filter
            .Eq(x => x.Week, 0);

        return await _collection
            .Find(filter)
            .ToListAsync(CancellationToken.None);
    }
}
