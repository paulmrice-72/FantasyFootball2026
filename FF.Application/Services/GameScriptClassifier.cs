// FF.Application/Services/GameScriptClassifier.cs
using FF.Domain.Enums;
using FF.Domain.ValueObjects;

namespace FF.Application.Services;

/// <summary>
/// Classifies expected game flow and produces positional volume multipliers
/// based on Vegas spread. When spread data is unavailable (spread = 0),
/// returns neutral Competitive script with 1.0 multipliers.
/// </summary>
public static class GameScriptClassifier
{
    // Thresholds from PBI-DIFF-002 addendum
    private const decimal BlowoutThreshold = 10m;   // favourite by 10+
    private const decimal TrailingThreshold = -10m;  // underdog by 10+

    // Volume multipliers per script (addendum spec)
    private const decimal BlowoutRbBoost = 1.12m;
    private const decimal BlowoutWrTeCut = 0.90m;
    private const decimal TrailingRbCut = 0.85m;
    private const decimal TrailingWrTeBoost = 1.12m;

    /// <summary>
    /// Classify game script from Vegas spread.
    /// Spread convention: positive = team is favourite (e.g. +7 means favoured by 7).
    /// </summary>
    public static CorrelationMetadata Classify(decimal spread)
    {
        var script = spread >= BlowoutThreshold ? GameScript.BlowoutWin
                   : spread <= TrailingThreshold ? GameScript.Trailing
                   : GameScript.Competitive;

        return script switch
        {
            GameScript.BlowoutWin => new CorrelationMetadata
            {
                Script = GameScript.BlowoutWin,
                Spread = spread,
                RbVolumeMultiplier = BlowoutRbBoost,
                WrTeVolumeMultiplier = BlowoutWrTeCut,
                QbWr1CorrelationCoefficient = 0.35m  // lower in blowout — less passing needed
            },
            GameScript.Trailing => new CorrelationMetadata
            {
                Script = GameScript.Trailing,
                Spread = spread,
                RbVolumeMultiplier = TrailingRbCut,
                WrTeVolumeMultiplier = TrailingWrTeBoost,
                QbWr1CorrelationCoefficient = 0.55m  // higher when trailing — more passing
            },
            _ => new CorrelationMetadata
            {
                Script = GameScript.Competitive,
                Spread = spread,
                RbVolumeMultiplier = 1.0m,
                WrTeVolumeMultiplier = 1.0m,
                QbWr1CorrelationCoefficient = 0.45m
            }
        };
    }

    /// <summary>
    /// Apply volume multiplier to a base projection based on position and script.
    /// </summary>
    public static decimal ApplyMultiplier(
        decimal baseProjection,
        string position,
        CorrelationMetadata correlation)
    {
        var multiplier = position switch
        {
            "RB" => correlation.RbVolumeMultiplier,
            "WR" or "TE" => correlation.WrTeVolumeMultiplier,
            _ => 1.0m  // QB and others unaffected
        };

        return Math.Max(0m, baseProjection * multiplier);
    }
}