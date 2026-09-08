using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

/// <summary>
/// Persistence for the positional value curves behind league-aware replacement
/// level — FAN-153. See <see cref="PositionalValueCurveDocument"/> for why the
/// curve is stored rather than the level.
/// </summary>
public interface IPositionalValueCurveRepository
{
    /// <summary>
    /// Every curve for one base season and scoring format — all projected years,
    /// all positions. This is the read the valuation pipeline wants: it needs the
    /// whole set at once to resolve a cutoff per year, and loading them
    /// individually would be one round trip per year per position.
    /// </summary>
    Task<List<PositionalValueCurveDocument>> GetAllBySeasonAndFormatAsync(
        int season, string scoringFormat, CancellationToken ct = default);

    /// <summary>One curve, for diagnostics and the admin surface.</summary>
    Task<PositionalValueCurveDocument?> GetAsync(
        int season, string scoringFormat, int year, string position, CancellationToken ct = default);

    Task UpsertBatchAsync(
        IEnumerable<PositionalValueCurveDocument> documents, CancellationToken ct = default);
}
