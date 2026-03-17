// FF.Domain/Entities/UserProjectionWeightProfile.cs
using FF.Domain.ValueObjects;
using FF.SharedKernel;

namespace FF.Domain.Entities;

public class UserProjectionWeightProfile : Entity
{
    public string AppUserId { get; private set; } = string.Empty;
    public string ProfileName { get; private set; } = "Default";
    public decimal RecentGameWeight { get; private set; } = 0.6m;
    public decimal SnapCountWeight { get; private set; } = 0.15m;
    public decimal MatchupWeight { get; private set; } = 0.25m;
    public int MinGamesRequired { get; private set; } = 3;
    public int LookbackWeeks { get; private set; } = 12;
    public bool IsActive { get; private set; } = true;

    private UserProjectionWeightProfile() { }

    public static UserProjectionWeightProfile CreateDefault(string appUserId) =>
        new()
        {
            AppUserId = appUserId,
            ProfileName = "Default",
            UpdatedAt = DateTime.UtcNow
        };

    public void Update(
        string profileName,
        decimal recentGameWeight,
        decimal snapCountWeight,
        decimal matchupWeight,
        int minGamesRequired,
        int lookbackWeeks)
    {
        ProfileName = profileName;
        RecentGameWeight = recentGameWeight;
        SnapCountWeight = snapCountWeight;
        MatchupWeight = matchupWeight;
        MinGamesRequired = minGamesRequired;
        LookbackWeeks = lookbackWeeks;
        UpdatedAt = DateTime.UtcNow;
    }

    public ProjectionWeightProfile ToWeightProfile() => new()
    {
        RecentGameWeight = RecentGameWeight,
        SnapCountWeight = SnapCountWeight,
        MatchupWeight = MatchupWeight,
        MinGamesRequired = MinGamesRequired,
        LookbackWeeks = LookbackWeeks
    };
}