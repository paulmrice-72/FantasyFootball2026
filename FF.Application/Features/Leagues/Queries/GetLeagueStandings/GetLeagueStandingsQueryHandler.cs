// FF.Application/Features/Leagues/Queries/GetLeagueStandings/GetLeagueStandingsQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Leagues.Queries.GetLeagueStandings;

public class GetLeagueStandingsQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    ISimulationResultRepository simulationRepository,
    IPlayerRepository playerRepository,
    ILeagueRepository leagueRepository,
    ILogger<GetLeagueStandingsQueryHandler> logger)
    : IRequestHandler<GetLeagueStandingsQuery, LeagueStandingsDto?>
{
    // Standard fantasy playoff cutoffs by league size
    private static int PlayoffTeams(int totalTeams) => totalTeams switch
    {
        <= 8  => 4,
        <= 10 => 4,
        12    => 6,
        14    => 6,
        _     => 4
    };

    public async Task<LeagueStandingsDto?> Handle(
        GetLeagueStandingsQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building standings for league {LeagueId} season {Season} week {Week}",
            request.SleeperLeagueId, request.Season, request.Week);

        // 1 — Load all rosters for this league
        var rosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);

        if (rosters.Count == 0)
            return null;

        // 2 — Load league for playoff cutoff
        var league = await leagueRepository
            .GetBySleeperIdAsync(
                request.SleeperLeagueId, request.Season, cancellationToken);

        var totalTeams    = league?.TotalTeams ?? rosters.Count;
        var playoffCutoff = PlayoffTeams(totalTeams);

        // 3 — Bulk load sim medians. Two different consumers need two
        // different slices of the same data:
        //   - starters only, for the displayed "Proj Pts This Week" figure
        //   - the FULL roster (every rostered player, not just starters),
        //     because RosterStrengthCalculator.ComputeRawDepthScore needs
        //     bench-eligible depth at each position, same as Roster Grades.
        var allStarterIds = rosters.SelectMany(r => r.StarterIds).Distinct().ToList();
        var allRosterIds  = rosters.SelectMany(r => r.PlayerIds).Distinct().ToList();

        var simResults = await simulationRepository
            .GetLatestBySleeperIdsAsync(allRosterIds, request.Season, cancellationToken);

        var simLookup = simResults
            .Where(s => s.SleeperPlayerId != null)
            .ToDictionary(s => s.SleeperPlayerId!, s => s);

        var simMedianLookup = simLookup.ToDictionary(kv => kv.Key, kv => (double)kv.Value.Median);

        var players = await playerRepository.GetBySleeperIdsAsync(allRosterIds, cancellationToken);
        var playerLookup = players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        // 4 — Build standings rows
        var teams = rosters.Select(roster =>
        {
            var projectedPoints = roster.StarterIds
                .Where(id => simLookup.ContainsKey(id))
                .Sum(id => simLookup[id].Median);

            // FAN-108: pre-season ranking now uses the SAME Depth Score as
            // Roster Grades (RosterStrengthCalculator), instead of a raw sum
            // of starter medians. The raw sum isn't normalized per position,
            // so it rewards whichever roster happens to have the highest-
            // scoring positions in its starting lineup rather than reflecting
            // overall roster strength — which is exactly why an "A" Roster
            // Grade team could show up ranked below a "D" team in Standings.
            // Using the shared calculator means the two tabs always agree
            // pre-season, per Paul's decision (2026-08-30 session).
            var depthScore = RosterStrengthCalculator.ComputeRawDepthScore(
                roster.PlayerIds, playerLookup, simMedianLookup);

            return new
            {
                Roster = roster,
                ProjectedPoints = projectedPoints,
                DepthScore = depthScore
            };
        }).ToList();

        // 5 — Sort: wins desc, then win% desc, then Depth Score desc as the
        // pre-season / no-games-yet tiebreaker (was: summed ProjectedPoints).
        var ranked = teams
            .OrderByDescending(t => t.Roster.Wins)
            .ThenByDescending(t => t.Roster.Losses == 0
                ? 999 : t.Roster.Wins / (double)(t.Roster.Wins + t.Roster.Losses))
            .ThenByDescending(t => t.DepthScore)
            .Select((t, index) =>
            {
                var rank = index + 1;

                var isFinalized = request.Week >= 15 || request.Season < DateTime.UtcNow.Year;
                var playoffProjection = isFinalized
                    ? (rank <= playoffCutoff ? "Clinched" : "Eliminated")
                    : rank <= playoffCutoff - 1
                        ? "In"
                        : rank == playoffCutoff || rank == playoffCutoff + 1
                            ? "Bubble"
                            : "Out";

                return new TeamStandingDto(
                    SleeperRosterId: t.Roster.SleeperRosterId,
                    TeamName: t.Roster.TeamName,
                    OwnerName: t.Roster.OwnerName,
                    Wins: t.Roster.Wins,
                    Losses: t.Roster.Losses,
                    Ties: t.Roster.Ties,
                    WaiverPosition: t.Roster.WaiverPosition,
                    Rank: rank,
                    ProjectedPointsThisWeek: Math.Round((decimal)t.ProjectedPoints, 1),
                    PointsFor: 0m,
                    PointsAgainst: 0m,
                    PlayoffProjection: playoffProjection);
            })
            .ToList();

        return new LeagueStandingsDto(
            SleeperLeagueId: request.SleeperLeagueId,
            Season:          request.Season,
            Week:            request.Week,
            Teams:           ranked);
    }
}
