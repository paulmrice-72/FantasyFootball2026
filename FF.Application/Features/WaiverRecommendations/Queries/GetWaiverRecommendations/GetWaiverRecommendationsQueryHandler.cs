// FF.Application/Features/WaiverRecommendations/Queries/GetWaiverRecommendations/GetWaiverRecommendationsQueryHandler.cs
using FF.Application.Common;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.WaiverRecommendations.Queries.GetWaiverRecommendations;

/// <summary>
/// Reads the stored VORP board for a league/week. FAN-118.
///
/// <para>
/// <b>This used to compute VORP itself, and write it, on every read.</b> That has
/// moved to <c>CalculateVorpCommand</c>, for four reasons beyond the architectural
/// one that a query should not persist:
/// </para>
///
/// <list type="number">
/// <item>
/// Replacement level came from a hardcoded table — QB 12, RB 24, WR 24, TE 12 —
/// identical for every league. A superflex league starts roughly thirty
/// quarterbacks, not twelve, so QB replacement was set at about the eleventh-best
/// QB in a format where the thirtieth is still a starter. Elite QB value was
/// understated by most of a round.
/// </item>
/// <item>
/// It read <c>ProjectedPoints</c> — the <b>standard, zero-PPR</b> cached column —
/// in a full-PPR league. Every receiver's value was computed without receptions.
/// Same class of defect as FAN-97, in a different place.
/// </item>
/// <item>
/// Replacement was taken as <c>ranked[slotCount - 1]</c>, which is the <i>worst
/// starter</i>, not the first player who would not start. Off by one, in the
/// direction that compresses everyone's VORP toward zero.
/// </item>
/// <item>
/// <c>FloorPoints = sim is not null ? sim.Floor : 0m</c> — a missing distribution
/// became a floor of exactly zero rather than no floor at all.
/// </item>
/// </list>
///
/// <para>
/// It also issued one <c>GetMostRecentBySleeperIdAsync</c> per projection — a query
/// per player, per page load, several hundred round trips.
/// </para>
///
/// <para>
/// The board is now produced by <c>CalculateVorpCommand</c> (Swagger:
/// <c>POST api/v1/waiver/vorp/calculate</c>, or the recurring job) and this handler
/// only reads it. An empty result means it has not been computed for that league and
/// week yet, which the UI reports rather than papering over.
/// </para>
/// </summary>
public class GetWaiverRecommendationsQueryHandler(
    IVorpRecommendationRepository vorpRepository,
    ICacheService cache)
    : IRequestHandler<GetWaiverRecommendationsQuery, IReadOnlyList<VorpRecommendationDocument>>
{
    public async Task<IReadOnlyList<VorpRecommendationDocument>> Handle(
        GetWaiverRecommendationsQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.VorpRecommendations(
            request.SleeperLeagueId, request.Season, request.Week,
            request.Position, request.Top);

        var cached = cache.Get<IReadOnlyList<VorpRecommendationDocument>>(cacheKey);
        if (cached is not null) return cached;

        var stored = await vorpRepository.GetByWeekAsync(
            request.SleeperLeagueId,
            request.Season,
            request.Week,
            request.Position,
            // Over-fetch, because the free-agent filter below removes rostered
            // players after the repository has already applied its own Take(top).
            request.Top * 4,
            cancellationToken);

        // The waiver board is about players you could actually add. Rostered players
        // are computed and stored too — rankings and Top Assets need them — but they
        // do not belong on this page.
        var available = stored
            .Where(d => !d.IsRostered)
            .Take(request.Top)
            .ToList();

        if (available.Count > 0)
            cache.Set(cacheKey, (IReadOnlyList<VorpRecommendationDocument>)available,
                      TimeSpan.FromMinutes(30));

        return available;
    }
}
