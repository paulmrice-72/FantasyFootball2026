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
        var points = 0m;

        points += passingYards * 0.04m;
        points += passingTds * passingTdPoints;
        points += interceptions * -2m;

        points += rushingYards * 0.1m;
        points += rushingTds * 6m;

        points += receptions * recPointsPerReception;
        points += receivingYards * 0.1m;
        points += receivingTds * 6m;
        points += receptions * bonusRecTe;

        points += fumblesLost * -2m;
        points += twoPointConversions * 2m;
        points += specialTeamsTds * 6m;

        return Math.Round(points, 2);
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
        // Aggregate fumbles lost across all categories
        var totalFumblesLost =
            log.RushingFumblesLost +
            log.ReceivingFumblesLost +
            log.SackFumblesLost;

        // Aggregate 2pt conversions across all categories
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