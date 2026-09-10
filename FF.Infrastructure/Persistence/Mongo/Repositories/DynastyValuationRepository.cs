// FF.Infrastructure/Persistence/Mongo/Repositories/DynastyValuationRepository.cs
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class DynastyValuationRepository(MongoDbContext context) : IDynastyValuationRepository
{
    private readonly IMongoCollection<DynastyValuationDocument> _collection =
        context.Database.GetCollection<DynastyValuationDocument>("dynasty_valuations");

    public async Task<DynastyValuationDocument?> GetBySleeperIdAsync(
        string sleeperPlayerId,
        CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, sleeperPlayerId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<List<DynastyValuationDocument>> GetByPositionAsync(
        string position,
        CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.Position, position);
        return await _collection.Find(filter)
            .SortByDescending(x => x.TradeValue)
            .ToListAsync(ct);
    }

    /// <summary>
    /// FAN-175. The scored filter and the tiebreaker below are on all three
    /// <c>GetTopBy*ValueAsync</c> selections, and they are one fix, not two.
    ///
    /// <para>
    /// <b>The filter.</b> These queries take the top N by value with no filter, so
    /// when a position has fewer scored players than N the limit runs off the end
    /// of the real data into the zeroed tail. Measured 2026-09-10: 115 scored QBs,
    /// 189 RBs, 201 TEs and 323 WRs against a harness asking for 250, so QB, RB and
    /// TE were padded with 135, 61 and 49 players who had been zeroed for an absence
    /// — no NFL team, or no career simulation. Some of them carry a FantasyPros
    /// dynasty rank, so they reached the matched subset and were graded, at the
    /// bottom of our ordering against a mid-range consensus rank.
    /// </para>
    ///
    /// <para>
    /// <b>The tiebreaker.</b> Those padding rows all hold exactly <c>0.0</c>, and a
    /// sort with no secondary key resolves ties in whatever order the storage engine
    /// returns them. So <i>which</i> zeroed players were graded differed between two
    /// runs over identical data, at an identical count — which is precisely the
    /// moving population FAN-175 was raised for, and why RB's graded population was
    /// 115, 111, 124 and 124 across four runs. Ties do not end with the padding: P2
    /// normalization rounds to two decimals, so real players collide too, and an
    /// unordered tie anywhere near the cutoff makes a run unreproducible. Sorting on
    /// a unique key last makes the selection total.
    /// </para>
    ///
    /// <para>
    /// Filtering on <c>IsScored</c> rather than on <c>value &gt; 0</c> is deliberate
    /// and is explained on <see cref="DynastyValuationDocument.IsScored"/>: the
    /// last-ranked scored player normalizes to exactly <c>0.00</c>, so a magnitude
    /// test drops one real player per run.
    /// </para>
    /// </summary>
    private static FilterDefinition<DynastyValuationDocument> ScoredInPosition(string? position)
    {
        var scored = Builders<DynastyValuationDocument>.Filter.Eq(x => x.IsScored, true);

        return position is null
            ? scored
            : Builders<DynastyValuationDocument>.Filter.And(
                scored,
                Builders<DynastyValuationDocument>.Filter.Eq(x => x.Position, position));
    }

    public async Task<List<DynastyValuationDocument>> GetTopByTradeValueAsync(
        int count,
        string? position = null,
        CancellationToken ct = default)
    {
        return await _collection.Find(ScoredInPosition(position))
            .SortByDescending(x => x.TradeValue)
            .ThenBy(x => x.SleeperPlayerId)
            .Limit(count)
            .ToListAsync(ct);
    }

    public async Task<List<DynastyValuationDocument>> GetTopByModelValueAsync(
        int count,
        string? position = null,
        CancellationToken ct = default)
    {
        // ModelValue is only written by DFV runs from 2026-09-07 onward. Rows
        // written before that have no field at all, which Mongo sorts as null —
        // below every real value on a descending sort, so a stale collection
        // yields an empty-looking comparison rather than a plausible wrong one.
        // The harness's own n < 10 guard turns that into a readable error.
        //
        // FAN-175: IsScored is newer still, written from 2026-09-10. A row from
        // before that has no field, which never equals true, so a stale collection
        // now returns nothing at all rather than a padded population — the same
        // fail-loud behaviour, one step earlier. See ScoredInPosition.
        return await _collection.Find(ScoredInPosition(position))
            .SortByDescending(x => x.ModelValue)
            .ThenBy(x => x.SleeperPlayerId)
            .Limit(count)
            .ToListAsync(ct);
    }

    public async Task<List<DynastyValuationDocument>> GetTopByRawValueAsync(
        int count,
        string? position = null,
        CancellationToken ct = default)
    {
        // FAN-166. Same selection rule as ModelValue and for the same reason:
        // rank on the basis you selected on. Selecting the top 250 by
        // ModelValue and re-sorting by RawValue would drop every player the
        // guardrail caps pushed out of the top 250 — which is precisely the
        // population this basis exists to look at, since a capped player is by
        // definition one the caps moved.
        //
        // RawValue is only written by DFV runs from 2026-09-08 onward. Older
        // rows have no field, which Mongo sorts as null and therefore below
        // every real value on a descending sort, so a stale collection produces
        // an empty-looking comparison the harness's n < 10 guard turns into a
        // readable error rather than a plausible wrong number.
        //
        // FAN-175: that reasoning was sound for a missing field and blind to a
        // present zero. Null sorts below every real value; 0.0 sorts below every
        // real value too, and there were several hundred of them per position, so
        // the limit reached them and the harness graded them. See ScoredInPosition.
        return await _collection.Find(ScoredInPosition(position))
            .SortByDescending(x => x.RawValue)
            .ThenBy(x => x.SleeperPlayerId)
            .Limit(count)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(
        DynastyValuationDocument document,
        CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

        var update = BuildUpdate(document);

        await _collection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            CancellationToken.None);
    }

    public async Task UpsertBatchAsync(
        IEnumerable<DynastyValuationDocument> documents,
        CancellationToken ct = default)
    {
        var docList = documents.ToList();
        if (docList.Count == 0) return;

        var writes = docList.Select(document =>
        {
            var filter = Builders<DynastyValuationDocument>.Filter
                .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

            return new UpdateOneModel<DynastyValuationDocument>(filter, BuildUpdate(document))
            {
                IsUpsert = true
            };
        }).Cast<WriteModel<DynastyValuationDocument>>().ToList();

        await _collection.BulkWriteAsync(
            writes,
            new BulkWriteOptions { IsOrdered = false },
            CancellationToken.None);
    }

    public async Task<List<DynastyValuationDocument>> GetBySleeperPlayerIdsAsync(
        IEnumerable<string> sleeperPlayerIds,
        CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .In(x => x.SleeperPlayerId, sleeperPlayerIds);
        return await _collection.Find(filter).ToListAsync(ct);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// FAN-175. Internal rather than private so a test can render it and assert it
    /// covers every property of the document. Three fields have now been computed,
    /// logged as correct, and dropped here — ModelValue (FAN-159), RawValue
    /// (FAN-166) and IsScored (FAN-175) — because an explicit per-field update list
    /// ignores anything nobody remembered to add, at both ends and in silence.
    /// </summary>
    internal static UpdateDefinition<DynastyValuationDocument> BuildUpdate(
        DynastyValuationDocument document)
    {
        var updateDefs = new List<UpdateDefinition<DynastyValuationDocument>>
        {
            Builders<DynastyValuationDocument>.Update.Set(x => x.PlayerId, document.PlayerId),
            Builders<DynastyValuationDocument>.Update.Set(x => x.Position, document.Position),
            Builders<DynastyValuationDocument>.Update.Set(x => x.NflTeam, document.NflTeam),
            Builders<DynastyValuationDocument>.Update.Set(x => x.Age, document.Age),
            Builders<DynastyValuationDocument>.Update.Set(x => x.YearsExperience, document.YearsExperience),
            Builders<DynastyValuationDocument>.Update.Set(x => x.Season, document.Season),
            Builders<DynastyValuationDocument>.Update.Set(x => x.ScoringFormat, document.ScoringFormat),
            Builders<DynastyValuationDocument>.Update.Set(x => x.BreakoutScore, document.BreakoutScore),
            Builders<DynastyValuationDocument>.Update.Set(x => x.BreakoutClassification, document.BreakoutClassification),
            Builders<DynastyValuationDocument>.Update.Set(x => x.BreakoutSignals, document.BreakoutSignals),
            Builders<DynastyValuationDocument>.Update.Set(x => x.BreakoutScoredAt, document.BreakoutScoredAt),
            Builders<DynastyValuationDocument>.Update.Set(x => x.TradeValue, document.TradeValue),
            Builders<DynastyValuationDocument>.Update.Set(x => x.DiscountedFutureValue, document.DiscountedFutureValue),
            // FAN-159 — without this the field is computed, held in memory, and
            // dropped at the repository boundary. That is the same silent-no-op
            // shape as FAN-138/140/141: the pipeline logs success, the harness
            // reads nulls, and nothing anywhere reports a problem.
            Builders<DynastyValuationDocument>.Update.Set(x => x.ModelValue, document.ModelValue),
            // FAN-166 — same reason as the line above it. A value that is
            // computed and then dropped at the repository boundary is the
            // failure mode this codebase keeps rediscovering.
            Builders<DynastyValuationDocument>.Update.Set(x => x.RawValue, document.RawValue),
            // FAN-175 — third time. The two comments above this one both describe
            // a value computed, held in memory, and dropped here; this line was
            // omitted anyway, and the symptom was every calibration basis throwing
            // "no scored valuations were selected" because IsScored reached no row
            // in the collection. An explicit per-field update list silently ignores
            // every property nobody remembered to add, so the omission is invisible
            // at both ends: the DFV run logs a correct scored population, and the
            // selection reads documents that have no such field.
            //
            // BuildUpdateCoversEveryProperty in FF.Tests now fails on the next
            // omission rather than leaving it to be found by a downstream throw.
            Builders<DynastyValuationDocument>.Update.Set(x => x.IsScored, document.IsScored),
            Builders<DynastyValuationDocument>.Update.Set(x => x.TradeValueComputedAt, document.TradeValueComputedAt),
            Builders<DynastyValuationDocument>.Update.Set(x => x.CareerValueScore, document.CareerValueScore),
            Builders<DynastyValuationDocument>.Update.Set(x => x.PeakYear, document.PeakYear),
            Builders<DynastyValuationDocument>.Update.Set(x => x.YearsOfPrimeRemaining, document.YearsOfPrimeRemaining),
            Builders<DynastyValuationDocument>.Update.Set(x => x.CareerPhase, document.CareerPhase),
            Builders<DynastyValuationDocument>.Update.SetOnInsert(x => x.Id, document.Id),
        };

        // Guard: only overwrite PlayerName if incoming value is valid
        if (!string.IsNullOrEmpty(document.PlayerName))
            updateDefs.Add(Builders<DynastyValuationDocument>.Update
                .Set(x => x.PlayerName, document.PlayerName));

        return Builders<DynastyValuationDocument>.Update.Combine(updateDefs);
    }
}