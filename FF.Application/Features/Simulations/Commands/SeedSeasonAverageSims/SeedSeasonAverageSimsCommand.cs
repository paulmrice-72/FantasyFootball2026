// FF.Application/Features/Simulations/Commands/SeedSeasonAverageSims/SeedSeasonAverageSimsCommand.cs
using MediatR;

namespace FF.Application.Features.Simulations.Commands.SeedSeasonAverageSims;

/// <summary>
/// Seeds season-average sim data from nflverse player_stats CSV.
/// CsvContent: optional — if provided, skips nflverse download (use for weekly file uploads).
/// Stores results as Season={season}, Week=0 (the season-average sentinel).
/// Half-PPR: fantasy_points + (receptions * 0.5) / games.
/// </summary>
public record SeedSeasonAverageSimsCommand(int Season, string? CsvContent = null)
    : IRequest<SeedSeasonAverageSimsResult>;

/// <summary>
/// Outcome of a seed run. The match counters exist because the identity path
/// this job takes is not obvious from the outside: <see cref="MatchedByGsis"/>
/// near zero means the stable-id bridge is broken and every row is being
/// resolved by NAME, which is where players get bound to the wrong Sleeper id.
/// Watch that ratio, not just <see cref="Seeded"/>.
/// </summary>
public record SeedSeasonAverageSimsResult(
    int Seeded,
    int Skipped,
    int Unmatched,
    int MatchedByGsis = 0,
    int MatchedByName = 0,
    int AmbiguousSkipped = 0);

/// <summary>
/// The nflverse CSV for a season could not be retrieved.
///
/// <see cref="NotPublished"/> distinguishes the two cases that used to be
/// collapsed into one message. It is TRUE only when nflverse answered and said
/// the file does not exist (404) — the normal preseason state. It is FALSE for
/// a timeout, DNS failure, or server error, which mean the data may well exist
/// and this environment simply could not reach it. Reporting the second as the
/// first is how a real egress problem hides behind "the season hasn't started".
/// </summary>
public class NflverseDataUnavailableException(
    int season,
    bool notPublished,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public int Season { get; } = season;
    public bool NotPublished { get; } = notPublished;
}
