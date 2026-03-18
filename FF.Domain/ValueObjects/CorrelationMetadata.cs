// FF.Domain/ValueObjects/CorrelationMetadata.cs
using FF.Domain.Enums;

namespace FF.Domain.ValueObjects;

public class CorrelationMetadata
{
    public GameScript Script { get; set; } = GameScript.Unknown;
    public decimal Spread { get; set; } = 0m;
    public decimal RbVolumeMultiplier { get; set; } = 1.0m;
    public decimal WrTeVolumeMultiplier { get; set; } = 1.0m;
    public decimal QbWr1CorrelationCoefficient { get; set; } = 0m;

    public static CorrelationMetadata Neutral() => new()
    {
        Script = GameScript.Competitive,
        Spread = 0m,
        RbVolumeMultiplier = 1.0m,
        WrTeVolumeMultiplier = 1.0m,
        QbWr1CorrelationCoefficient = 0.45m
    };
}