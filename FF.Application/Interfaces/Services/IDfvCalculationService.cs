using FF.Domain.Documents;
using FF.Domain.Enums;

namespace FF.Application.Interfaces.Services;

public interface IDfvCalculationService
{
    /// <summary>
    /// Calculates DFV for all players and normalizes to 0-100 scale across
    /// all positions. ScoringFormat drives position scarcity multipliers
    /// (superflex formats heavily boost QB value).
    /// </summary>
    Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr,
        CancellationToken ct = default);

    /// <summary>
    /// Raw (un-normalized) DFV for a single player. Used in trade analyzer.
    /// </summary>
    double CalculateRawDfv(
        CareerSimulationDocument careerSim,
        string position,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr);
}