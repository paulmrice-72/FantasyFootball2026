using FF.Domain.Documents;
using FF.Domain.Enums;

namespace FF.Application.Interfaces.Repositories;

public interface IDynastyValuationRepository
{
    Task<DynastyValuationDocument?> GetBySleeperIdAsync(string sleeperPlayerId, CancellationToken ct = default);
    Task<List<DynastyValuationDocument>> GetByPositionAsync(string position, CancellationToken ct = default);
    Task<List<DynastyValuationDocument>> GetTopByTradeValueAsync(int count, string? position = null, CancellationToken ct = default);

    /// <summary>
    /// FAN-159. Top N by <see cref="DynastyValuationDocument.ModelValue"/> — the
    /// pipeline's answer with the FantasyPros blend removed.
    ///
    /// <para>
    /// This exists because the calibration harness cannot reuse
    /// <see cref="GetTopByTradeValueAsync"/> and simply re-sort. The selection is
    /// part of the measurement: taking the top 250 by TradeValue and then ranking
    /// them by ModelValue silently drops every player the FP blend pushed out of
    /// the top 250, which is precisely the population where model and consensus
    /// disagree most — the sample would be biased toward agreement by the same
    /// mechanism the ticket was raised about. Rank on the basis you selected on.
    /// </para>
    /// </summary>
    Task<List<DynastyValuationDocument>> GetTopByModelValueAsync(int count, string? position = null, CancellationToken ct = default);

    /// <summary>
    /// FAN-166. Top N by <see cref="DynastyValuationDocument.RawValue"/> — the
    /// same pipeline one step before <see cref="GetTopByModelValueAsync"/>,
    /// with the positional guardrail caps not yet applied.
    ///
    /// <para>
    /// The selection rule is the same as ModelValue's and exists for the same
    /// reason: selecting on one basis and ranking on another biases the sample
    /// by exactly the mechanism under measurement. Here the population of
    /// interest is the players the caps moved, and those are precisely the ones
    /// a ModelValue-ordered top 250 would drop.
    /// </para>
    /// </summary>
    Task<List<DynastyValuationDocument>> GetTopByRawValueAsync(int count, string? position = null, CancellationToken ct = default);

    Task UpsertAsync(DynastyValuationDocument document, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<DynastyValuationDocument> documents, CancellationToken ct = default);

    // ── E10 Dynasty Draft ─────────────────────────────────────────────────
    Task<List<DynastyValuationDocument>> GetBySleeperPlayerIdsAsync(
        IEnumerable<string> sleeperPlayerIds, CancellationToken ct = default);
}