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

public record SeedSeasonAverageSimsResult(int Seeded, int Skipped, int Unmatched);