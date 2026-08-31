// FF.Application/Features/Leagues/Queries/GetRedraftRosterGrades/GetRedraftRosterGradesQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Leagues.Queries.GetRedraftRosterGrades;

// FAN-107 (2026-08-30): redraft counterpart to GetLeagueRosterGradesQueryHandler.
// Shares the exact same Depth Score calculation (RosterStrengthCalculator) so
// the two handlers can't drift apart, but drops every dynasty-only concept —
// no DynastyScore, TeamProfile, DraftCapitalScore, or OwnedPickCount, since
// none of those mean anything in a one-year league.
public class GetRedraftRosterGradesQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    ILogger<GetRedraftRosterGradesQueryHandler> logger)
    : IRequestHandler<GetRedraftRosterGradesQuery, RedraftLeagueRosterGradesDto?>
{
    public async Task<RedraftLeagueRosterGradesDto?> Handle(
        GetRedraftRosterGradesQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building redraft roster grades for league {LeagueId} season {Season}",
            request.SleeperLeagueId, request.Season);

        var rosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);
        if (rosters.Count == 0) return null;

        var allPlayerIds = rosters.SelectMany(r => r.PlayerIds).Distinct().ToList();

        var simDocs = await simulationRepository
            .GetLatestBySleeperIdsAsync(allPlayerIds, request.Season, cancellationToken);
        var simLookup = simDocs
            .Where(s => s.SleeperPlayerId != null)
            .ToDictionary(s => s.SleeperPlayerId!, s => (double)s.Median);

        var players = await playerRepository.GetBySleeperIdsAsync(allPlayerIds, cancellationToken);
        var playerLookup = players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        var rawTeams = rosters.Select(roster =>
        {
            // Depth Score — identical calculation to the dynasty handler,
            // via the shared calculator (FAN-107).
            var depthScore = RosterStrengthCalculator.ComputeRawDepthScore(
                roster.PlayerIds, playerLookup, simLookup);

            // Top assets by sim median — same "top 5 skill-position players
            // by projected production" idea as the dynasty handler's Top
            // Assets, just reported as season-average points instead of
            // TradeValue/BreakoutScore (no dynasty valuations apply here).
            var topAssets = roster.PlayerIds
                .Where(id => simLookup.ContainsKey(id) && playerLookup.ContainsKey(id))
                .Select(id => (Id: id, Median: simLookup[id], Player: playerLookup[id]))
                .Where(x => new[] { "QB", "RB", "WR", "TE" }.Contains(x.Player.Position.ToString()))
                .OrderByDescending(x => x.Median)
                .Take(5)
                .Select(x => new RedraftTeamAssetDto(
                    PlayerName: x.Player.FullName ?? "Unknown",
                    Position: x.Player.Position.ToString() ?? "—",
                    SeasonAvgPoints: Math.Round(x.Median, 1)))
                .ToList();

            return (roster.SleeperRosterId, roster.TeamName, roster.OwnerName,
                DepthScore: depthScore, TopAssets: topAssets);
        })
        .ToList();

        // Grade + rank relative to this league — same rationale as the
        // dynasty handler: self-correcting as the underlying score scale
        // moves, and it's what "where do I stand among the other teams"
        // actually means. See RosterStrengthCalculator.RankFractionToGrade.
        var depthRank = RosterStrengthCalculator.RankByDescending(rawTeams, t => t.DepthScore);

        var teams = rawTeams
            .Select((t, idx) => new RedraftTeamRosterGradeDto(
                SleeperRosterId: t.SleeperRosterId,
                TeamName: t.TeamName,
                OwnerName: t.OwnerName,
                Rank: 0, // stamped for real just below, once sorted
                DepthGrade: RosterStrengthCalculator.RankFractionToGrade(depthRank[idx]),
                DepthScore: Math.Round(t.DepthScore, 1),
                TopAssets: t.TopAssets))
            .OrderByDescending(t => t.DepthScore)
            .Select((t, idx) => t with { Rank = idx + 1 })
            .ToList();

        return new RedraftLeagueRosterGradesDto(
            SleeperLeagueId: request.SleeperLeagueId,
            Season: request.Season,
            Teams: teams);
    }
}
