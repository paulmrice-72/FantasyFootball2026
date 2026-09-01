// FF.Application/Services/FantasyPointsCalculator.cs
//
// Legacy entry point, kept so existing call sites compile unchanged.
// The math now lives in FantasyScoringService (L1 of Epic 20) — this class only
// adapts loose parameters into a LeagueScoringSettings. New code should call
// FantasyScoringService directly with the league's own settings.
using FF.Domain.ValueObjects;

namespace FF.Application.Services;

public static class FantasyPointsCalculator
{
    /// <summary>
    /// Calculates fantasy points for a raw stat line.
    /// </summary>
    public static decimal Calculate(
        decimal passingYards = 0,
        decimal passingTds = 0,
        decimal interceptions = 0,
        decimal rushingYards = 0,
        decimal rushingTds = 0,
        decimal receptions = 0,
        decimal receivingYards = 0,
        decimal receivingTds = 0,
        decimal fumblesLost = 0,
        decimal twoPointConversions = 0,
        decimal specialTeamsTds = 0,
        decimal recPointsPerReception = 1m,
        decimal passingTdPoints = 4m,
        decimal bonusRecTe = 0m)
    {
        var settings = LeagueScoringSettings.From(
            recPointsPerReception, passingTdPoints, bonusRecTe);

        return FantasyScoringService.Score(
            settings,
            passingYards: passingYards,
            passingTds: passingTds,
            interceptions: interceptions,
            rushingYards: rushingYards,
            rushingTds: rushingTds,
            receptions: receptions,
            receivingYards: receivingYards,
            receivingTds: receivingTds,
            fumblesLost: fumblesLost,
            twoPointConversions: twoPointConversions,
            specialTeamsTds: specialTeamsTds,
            // Legacy behaviour: the caller decided whether the TE bonus applies,
            // so pass it through verbatim rather than gating on position.
            bonusRecTeOverride: bonusRecTe);
    }

    /// <summary>
    /// Calculates fantasy points directly from a PlayerGameLogDocument.
    /// Aggregates fumbles lost and 2pt conversions across all categories.
    /// </summary>
    public static decimal Calculate(
        Domain.Documents.PlayerGameLogDocument log,
        decimal recPointsPerReception = 1m,
        decimal passingTdPoints = 4m,
        decimal bonusRecTe = 0m)
    {
        var totalFumblesLost =
            log.RushingFumblesLost +
            log.ReceivingFumblesLost +
            log.SackFumblesLost;

        var total2Pt =
            log.Passing2PtConversions +
            log.Rushing2PtConversions +
            log.Receiving2PtConversions;

        return Calculate(
            passingYards: log.PassingYards,
            passingTds: log.PassingTds,
            interceptions: log.Interceptions,
            rushingYards: log.RushingYards,
            rushingTds: log.RushingTds,
            receptions: log.Receptions,
            receivingYards: log.ReceivingYards,
            receivingTds: log.ReceivingTds,
            fumblesLost: totalFumblesLost,
            twoPointConversions: total2Pt,
            specialTeamsTds: log.SpecialTeamsTds,
            recPointsPerReception: recPointsPerReception,
            passingTdPoints: passingTdPoints,
            bonusRecTe: bonusRecTe);
    }
}
