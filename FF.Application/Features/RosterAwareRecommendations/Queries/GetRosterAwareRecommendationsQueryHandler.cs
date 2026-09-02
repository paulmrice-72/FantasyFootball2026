// FF.Application/QueryHandlers/GetRosterAwareRecommendationsQueryHandler.cs
using FF.Application.Services;
using FF.Application.Interfaces.Persistence;
using MediatR;
using FF.Domain.Documents;

namespace FF.Application.Features.RosterAwareRecommendations.Queries;

public class GetRosterAwareRecommendationsQueryHandler(
    IVorpRecommendationRepository vorpRepository,
    RosterProfileService rosterProfileService)
    : IRequestHandler<GetRosterAwareRecommendationsQuery,
        IReadOnlyList<RosterAwareRecommendation>>
{
    // Fit multipliers applied to VORP score based on roster need
    private const decimal NeedMultiplier = 1.30m;  // position thin — boost
    private const decimal NeutralMultiplier = 1.00m;  // average depth — no change
    private const decimal StrengthMultiplier = 0.75m;  // well-stocked — discount

    public async Task<IReadOnlyList<RosterAwareRecommendation>> Handle(
        GetRosterAwareRecommendationsQuery request,
        CancellationToken cancellationToken)
    {
        // Load the pre-computed VORP board for this league and week.
        // FAN-118: league-scoped — the board now holds one row per player PER LEAGUE,
        // because both replacement baselines depend on the league.
        var board = await vorpRepository.GetByWeekAsync(
            request.SleeperLeagueId,
            request.Season, request.Week,
            request.Position,
            request.Top * 6,   // wide pool: rostered players and fit filtering both cut it down
            cancellationToken);

        // The board deliberately includes rostered players — rankings and Top Assets
        // need the whole pool. This page is about players you could actually add, so
        // they come out here.
        var vorpRecs = board.Where(r => !r.IsRostered).ToList();

        if (vorpRecs.Count == 0)
            return [];

        // Build roster profile for this user
        var profile = await rosterProfileService.BuildAsync(
            request.SleeperLeagueId,
            request.SleeperUserId,
            request.Season,
            request.Week,
            cancellationToken);

        // If we can't resolve the roster, fall back to plain VORP order
        if (profile is null)
        {
            return vorpRecs
                .Take(request.Top)
                .Select((r, i) => new RosterAwareRecommendation(
                    r, r.Vorp, RosterNeed.Neutral, i + 1))
                .ToList();
        }

        // Apply fit multiplier to each recommendation
        var scored = vorpRecs
            .Where(r => r.Vorp > 0)
            .Select(r =>
            {
                var need = profile.NeedByPosition.TryGetValue(r.Position, out var n)
                    ? n
                    : RosterNeed.Neutral;

                var multiplier = need switch
                {
                    RosterNeed.Need => NeedMultiplier,
                    RosterNeed.Strength => StrengthMultiplier,
                    _ => NeutralMultiplier
                };

                return new RosterAwareRecommendation(
                    r,
                    Math.Round(r.Vorp * multiplier, 2),
                    need,
                    0);  // rank assigned below
            })
            .OrderByDescending(r => r.FitScore)
            .Take(request.Top)
            .ToList();

        // Assign fit rank
        return scored
            .Select((r, i) => r with { FitRank = i + 1 })
            .ToList();
    }
}