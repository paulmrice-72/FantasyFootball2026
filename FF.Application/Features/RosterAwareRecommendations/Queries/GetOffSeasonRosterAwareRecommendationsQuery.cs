using FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.RosterAwareRecommendations.Queries;

// FAN-113 (2026-08-30): pre-season counterpart to GetRosterAwareRecommendationsQuery.
// The real handler returns [] whenever no VORP recommendations have been
// persisted yet (i.e. before Week 1 projections are calculated) — there was
// no fallback at all for "My Roster Fit", unlike the main Available Players
// tab, which got an ADP/Dynasty-Value fallback in FAN-112. This reuses that
// same off-season pool (GetOffSeasonAvailablePlayersQuery) and applies the
// identical Need/Neutral/Strength fit multiplier from
// GetRosterAwareRecommendationsQueryHandler, so Roster Fit isn't just blank
// pre-season.
public record GetOffSeasonRosterAwareRecommendationsQuery(
    string SleeperLeagueId,
    string SleeperUserId,
    string? Position = null,
    int Top = 30) : IRequest<IReadOnlyList<OffSeasonRosterAwareRecommendation>>;

public record OffSeasonRosterAwareRecommendation(
    OffSeasonAvailablePlayerDto Base,
    decimal FitScore,
    RosterNeed PositionNeed,
    int FitRank);
