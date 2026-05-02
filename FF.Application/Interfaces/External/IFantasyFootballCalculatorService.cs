// FF.Application/Interfaces/External/IFantasyFootballCalculatorService.cs
namespace FF.Application.Interfaces.External;

/// <summary>
/// Fetches consensus ADP data from the Fantasy Football Calculator free REST API.
/// Endpoint: https://fantasyfootballcalculator.com/api/v1/adp/{format}?teams={n}&year={year}
/// Attribution required on any UI surface displaying this data.
/// </summary>
public interface IFantasyFootballCalculatorService
{
    /// <summary>
    /// Returns ADP entries for the given season and scoring format.
    /// </summary>
    Task<IReadOnlyList<FfcPlayerAdp>> GetAdpAsync(
        int season,
        string scoringFormat = "ppr",
        int teamCount = 12,
        CancellationToken ct = default);
}

/// <summary>Parsed ADP entry from FFC API response.</summary>
public record FfcPlayerAdp(
    string Name,
    string Position,
    string? Team,
    double Adp,
    int AdpRound,
    int PickCount);