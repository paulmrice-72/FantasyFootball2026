// FF.Domain/ValueObjects/ProjectionWeightProfile.cs
namespace FF.Domain.ValueObjects;

public record ProjectionWeightProfile
{
    public decimal RecentGameWeight { get; init; } = 0.6m;   // weight given to L4 games vs full season
    public decimal SnapCountWeight { get; init; } = 0.15m;   // snap% influence on projection
    public decimal MatchupWeight { get; init; } = 0.25m;     // defensive ranking influence
    public int MinGamesRequired { get; init; } = 4;
    public int LookbackWeeks { get; init; } = 8;

    public static ProjectionWeightProfile Default => new();
}