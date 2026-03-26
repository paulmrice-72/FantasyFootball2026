using FF.Domain.Documents;

namespace FF.Application.Interfaces.Services;

public interface IDfvCalculationService
{
    /// <summary>
    /// Calculates DFV for all players and normalizes to 0-100 scale.
    /// Normalization requires the full player set — cannot be done per-player.
    /// </summary>
    Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season, CancellationToken ct = default);

    /// <summary>
    /// Raw (un-normalized) DFV for a single player. Used in trade analyzer
    /// where we compare two sides without re-normalizing the full population.
    /// </summary>
    double CalculateRawDfv(CareerSimulationDocument careerSim, string position);
}