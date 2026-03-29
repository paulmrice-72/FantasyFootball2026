// FF.Domain/Documents/RosterAwareRecommendation.cs

namespace FF.Domain.Documents;

public enum RosterNeed
{
    Strength,
    Neutral,
    Need
}

public record RosterAwareRecommendation(
    VorpRecommendationDocument Base,
    decimal FitScore,
    RosterNeed PositionNeed,
    int FitRank);