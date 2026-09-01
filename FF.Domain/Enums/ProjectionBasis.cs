// FF.Domain/Enums/ProjectionBasis.cs
namespace FF.Domain.Enums;

/// <summary>
/// What a projection was actually built from.
///
/// Exists because the app previously fell back to the prior season silently:
/// every grade, ranking and matchup number was a 2025 average presented as current,
/// with nothing in the data or the UI saying so. Stamping the basis on the document
/// makes staleness a first-class, queryable fact instead of an invisible one.
/// </summary>
public enum ProjectionBasis
{
    /// <summary>Nothing to project from — no usable game logs in any season.</summary>
    None = 0,

    /// <summary>Built from the requested season's own game logs.</summary>
    CurrentSeason = 1,

    /// <summary>
    /// Built from the prior season's game logs because the requested season has none
    /// yet (preseason / Week 0). Honest but stale — surface it in the UI.
    /// </summary>
    PriorSeasonCarryover = 2,

    /// <summary>
    /// No NFL game logs at all — projected from draft capital, depth chart and
    /// athletic profile. Reserved for PROJ-004; not produced by PROJ-001.
    /// </summary>
    RookieProjection = 3
}
