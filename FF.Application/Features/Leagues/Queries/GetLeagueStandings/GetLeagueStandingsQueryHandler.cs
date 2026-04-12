// FF.Application/Features/Leagues/Queries/GetLeagueStandings/GetLeagueStandingsQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Leagues.Queries.GetLeagueStandings;

public class GetLeagueStandingsQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    ISimulationResultRepository simulationRepository,
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

        // 3 — Bulk load projected points for all starters across all rosters
        var allStarterIds = rosters
            .SelectMany(r => r.StarterIds)
            .Distinct()
            .ToList();

        var simResults = await simulationRepository
            .GetLatestBySleeperIdsAsync(
                allStarterIds, request.Season, cancellationToken);

        var simLookup = simResults
            .Where(s => s.SleeperPlayerId != null)
            .ToDictionary(s => s.SleeperPlayerId!, s => s);

        // 4 — Build standings rows
        // TO:
        var teams = rosters.Select(roster =>
        {
            var projectedPoints = roster.StarterIds
                .Where(id => simLookup.ContainsKey(id))
                .Sum(id => simLookup[id].Median);

            return new
            {
                Roster = roster,
                ProjectedPoints = projectedPoints
            };
        }).ToList();

        // 5 — Sort: wins desc, then projected points desc as tiebreaker
        var ranked = teams
            .OrderByDescending(t => t.Roster.Wins)
            .ThenByDescending(t => t.Roster.Losses == 0
                ? 999 : t.Roster.Wins / (double)(t.Roster.Wins + t.Roster.Losses))
            .ThenByDescending(t => t.ProjectedPoints)
            .Select((t, index) =>
            {
                var rank = index + 1;

                // NEW
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