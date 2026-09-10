using FF.Domain.Documents;
using FF.Domain.Enums;

namespace FF.Application.Interfaces.Services;

public interface IDfvCalculationService
{
    /// <summary>
    /// Calculates DFV for all players and normalizes to 0-100 scale across
    /// all positions. ScoringFormat selects the league shape and the stored
    /// positional value curves used to resolve replacement level.
    /// </summary>
    /// <param name="disableVor">
    /// FAN-153 control experiment. When true, a player's raw value is his plain
    /// discounted career total — no replacement subtraction, and no positional
    /// scarcity multiplier either (those were deleted in phase 2 and are not
    /// resurrected here). Every other stage of the pipeline is untouched.
    ///
    /// <para>
    /// This is a measurement path, not a serving path. Its purpose is that a
    /// per-position multiplicative constant and a per-position additive constant
    /// are both monotone within a position, and P2 normalization is rank-based —
    /// so within-position ordering under this flag is identical to both the
    /// 09-08 pipeline and the phase 2 pipeline by construction. Any
    /// within-position rho that differs between the three is therefore
    /// attributable to the calibration population or to upstream drift, and
    /// never to the arithmetic. See FAN-175.
    /// </para>
    /// </param>
    Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr,
        bool disableVor = false,
        CancellationToken ct = default);

    /// <summary>
    /// Raw (un-normalized) DFV for a single player. Used in trade analyzer.
    /// </summary>
    double CalculateRawDfv(
        CareerSimulationDocument careerSim,
        string position,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr);
}