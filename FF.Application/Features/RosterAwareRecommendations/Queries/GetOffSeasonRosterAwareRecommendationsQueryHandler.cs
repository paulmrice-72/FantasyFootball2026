using FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.RosterAwareRecommendations.Queries;

public class GetOffSeasonRosterAwareRecommendationsQueryHandler(
    IMediator mediator,
    RosterProfileService rosterProfileService,
    INflContextService nflContext)
    : IRequestHandler<GetOffSeasonRosterAwareRecommendationsQuery,
        IReadOnlyList<OffSeasonRosterAwareRecommendation>>
{
    // Same fit multipliers as GetRosterAwareRecommendationsQueryHandler —
    // keep the two in sync if either changes.
    private const decimal NeedMultiplier = 1.30m;      // position thin — boost
    private const decimal NeutralMultiplier = 1.00m;   // average depth — no change
    private const decimal StrengthMultiplier = 0.75m;  // well-stocked — discount

    // ADP is "lower is better" (pick 1.2 beats pick 45.6); Dynasty Value is
    // "higher is better". The fit multiplier assumes "higher score = better",
    // so ADP has to be inverted to a positive, monotonically-increasing
    // goodness score before the multiplier is applied — otherwise boosting a
    // thin position would push its players' raw ADP number UP (later pick,
    // worse), the opposite of the intent. 300 is comfortably above any
    // realistic redraft ADP (12-team, ~20-round deep), just far enough out
    // that "ceiling - Adp" always stays positive.
    private const decimal AdpInversionCeiling = 300m;

    public async Task<IReadOnlyList<OffSeasonRosterAwareRecommendation>> Handle(
        GetOffSeasonRosterAwareRecommendationsQuery request,
        CancellationToken cancellationToken)
    {
        // Reuse the same off-season pool (dynasty value or ADP, whichever the
        // league type resolves to) the Available Players tab already uses.
        var pool = await mediator.Send(
            new GetOffSeasonAvailablePlayersQuery(
                request.SleeperLeagueId, request.Position, request.Top * 3),
            cancellationToken);

        if (pool.Count == 0)
            return [];

        var (season, week) = await nflContext.GetContextAsync();

        var profile = await rosterProfileService.BuildAsync(
            request.SleeperLeagueId, request.SleeperUserId, season, week, cancellationToken);

        // Can't resolve this user's roster — fall back to plain off-season order
        if (profile is null)
        {
            return pool
                .Take(request.Top)
                .Select((p, i) => new OffSeasonRosterAwareRecommendation(
                    p, (decimal)p.Value, RosterNeed.Neutral, i + 1))
                .ToList();
        }

        var scored = pool
            .Select(p =>
            {
                var need = profile.NeedByPosition.TryGetValue(p.Position, out var n)
                    ? n
                    : RosterNeed.Neutral;

                var multiplier = need switch
                {
                    RosterNeed.Need => NeedMultiplier,
                    RosterNeed.Strength => StrengthMultiplier,
                    _ => NeutralMultiplier
                };

                var goodness = p.ValueLabel == "ADP"
                    ? AdpInversionCeiling - (decimal)p.Value
                    : (decimal)p.Value;

                return new OffSeasonRosterAwareRecommendation(
                    p, Math.Round(goodness * multiplier, 2), need, 0); // rank assigned below
            })
            .OrderByDescending(r => r.FitScore)
            .Take(request.Top)
            .ToList();

        return scored
            .Select((r, i) => r with { FitRank = i + 1 })
            .ToList();
    }
}
