// FF.Application/Services/FantasyScoringService.cs
using FF.Domain.Documents;
using FF.Domain.ValueObjects;

namespace FF.Application.Services;

/// <summary>
/// L1 of the Unified Projection Engine (Epic 20 / FAN-116).
///
/// The single place where football units become fantasy points. Everything that
/// needs points — rankings, roster grades, Monte Carlo, trade analysis, matchups —
/// scores a <see cref="ProjectedStatLine"/> or a <see cref="PlayerGameLogDocument"/>
/// through here with the league's own <see cref="LeagueScoringSettings"/>.
///
/// This is what makes a fourth hardcoded ProjectedPointsXxx column unnecessary:
/// a new format is a new settings object, not a new schema field.
/// </summary>
public static class FantasyScoringService
{
    /// <summary>
    /// Scores an expected stat line in a league's format.
    /// The TE reception bonus is applied only for tight ends, so callers do not
    /// have to gate it themselves.
    /// </summary>
    public static decimal Score(
        ProjectedStatLine statLine,
        LeagueScoringSettings settings,
        string position)
    {
        if (statLine is null) return 0m;

        var teBonus = IsTightEnd(position) ? settings.BonusRecTe : 0m;

        return Score(
            settings,
            passingYards: statLine.PassingYards,
            passingTds: statLine.PassingTds,
            interceptions: statLine.Interceptions,
            rushingYards: statLine.RushingYards,
            rushingTds: statLine.RushingTds,
            receptions: statLine.Receptions,
            receivingYards: statLine.ReceivingYards,
            receivingTds: statLine.ReceivingTds,
            fumblesLost: statLine.FumblesLost,
            twoPointConversions: statLine.TwoPointConversions,
            specialTeamsTds: statLine.SpecialTeamsTds,
            bonusRecTeOverride: teBonus);
    }

    /// <summary>
    /// Scores an actual game log in a league's format. Used by calibration and by
    /// anything comparing projected vs actual, so both sides use identical math.
    /// </summary>
    public static decimal Score(
        PlayerGameLogDocument log,
        LeagueScoringSettings settings)
    {
        if (log is null) return 0m;

        var teBonus = IsTightEnd(log.Position) ? settings.BonusRecTe : 0m;

        var totalFumblesLost =
            log.RushingFumblesLost +
            log.ReceivingFumblesLost +
            log.SackFumblesLost;

        var total2Pt =
            log.Passing2PtConversions +
            log.Rushing2PtConversions +
            log.Receiving2PtConversions;

        return Score(
            settings,
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
            bonusRecTeOverride: teBonus);
    }

    /// <summary>
    /// Core scoring math. Every other overload funnels here — do not duplicate it.
    /// </summary>
    public static decimal Score(
        LeagueScoringSettings settings,
        decimal passingYards = 0m,
        decimal passingTds = 0m,
        decimal interceptions = 0m,
        decimal rushingYards = 0m,
        decimal rushingTds = 0m,
        decimal receptions = 0m,
        decimal receivingYards = 0m,
        decimal receivingTds = 0m,
        decimal fumblesLost = 0m,
        decimal twoPointConversions = 0m,
        decimal specialTeamsTds = 0m,
        decimal? bonusRecTeOverride = null)
    {
        settings ??= LeagueScoringSettings.HalfPpr;

        var points = 0m;

        points += passingYards * settings.PointsPerPassingYard;
        points += passingTds * settings.PassingTdPoints;
        points += interceptions * settings.InterceptionPoints;

        points += rushingYards * settings.PointsPerRushingYard;
        points += rushingTds * settings.RushingTdPoints;

        points += receptions * settings.PointsPerReception;
        points += receivingYards * settings.PointsPerReceivingYard;
        points += receivingTds * settings.ReceivingTdPoints;
        points += receptions * (bonusRecTeOverride ?? settings.BonusRecTe);

        points += fumblesLost * settings.FumbleLostPoints;
        points += twoPointConversions * settings.TwoPointConversionPoints;
        points += specialTeamsTds * settings.SpecialTeamsTdPoints;

        return Math.Round(points, 2);
    }

    private static bool IsTightEnd(string? position) =>
        string.Equals(position, "TE", StringComparison.OrdinalIgnoreCase);
}
