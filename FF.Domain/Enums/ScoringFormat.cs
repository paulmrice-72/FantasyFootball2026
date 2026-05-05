namespace FF.Domain.Enums;

public enum ScoringFormat
{
    Standard = 0,
    HalfPpr = 1,
    FullPpr = 2,
    Superflex = 3,       // Half-PPR + superflex QB slot
    SuperflexFullPpr = 4 // Full-PPR + superflex QB slot
}