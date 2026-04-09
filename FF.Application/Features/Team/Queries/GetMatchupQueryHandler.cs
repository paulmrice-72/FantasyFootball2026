// FF.Application/Features/Team/Queries/GetMyMatchupQueryHandler.cs
using FF.Application.Interfaces.External;
using FF.Application.Interfaces.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

public class GetMyMatchupQueryHandler(
    ISleeperMatchupService sleeperMatchupService,
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IInjuryAlertRepository injuryAlertRepository,
    ILogger<GetMyMatchupQueryHandler> logger)
    : IRequestHandler<GetMyMatchupQuery, MyMatchupDto?>
{
    public async Task<MyMatchupDto?> Handle(
        GetMyMatchupQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Loading matchup for user {SleeperUserId} league {LeagueId} week {Week}",
            request.SleeperUserId, request.SleeperLeagueId, request.Week);

        // 1 — Get all matchups for this week from Sleeper
        var matchups = await sleeperMatchupService.GetMatchupsAsync(
            request.SleeperLeagueId, request.Week, cancellationToken);

        if (matchups.Count == 0)
        {
            logger.LogWarning("No matchup data from Sleeper for league {LeagueId} week {Week}",
                request.SleeperLeagueId, request.Week);
            return null;
        }

        // 2 — Find the user's roster document to get their SleeperRosterId
        var myRosterDoc = await rosterPlayerRepository.GetBySleeperUserIdAsync(
            request.SleeperUserId, request.SleeperLeagueId, cancellationToken);

        if (myRosterDoc is null)
        {
            logger.LogWarning("Roster not found for user {SleeperUserId}", request.SleeperUserId);
            return null;
        }

        // 3 — Match user's roster to their Sleeper matchup entry
        var myMatchupEntry = matchups.FirstOrDefault(m =>
            m.RosterId.ToString() == myRosterDoc.SleeperRosterId);

        if (myMatchupEntry is null)
        {
            logger.LogWarning("No matchup entry found for roster {RosterId}",
                myRosterDoc.SleeperRosterId);
            return null;
        }

        // 4 — Find opponent's matchup entry (same matchup_id, different roster)
        var opponentEntry = matchups.FirstOrDefault(m =>
            m.MatchupId == myMatchupEntry.MatchupId &&
            m.RosterId != myMatchupEntry.RosterId);

        if (opponentEntry is null)
        {
            logger.LogWarning("No opponent found for matchup {MatchupId}", myMatchupEntry.MatchupId);
            return null;
        }

        // 5 — Get opponent's roster document (for team name/owner)
        var allRosterDocs = await rosterPlayerRepository.GetByLeagueAsync(
            request.SleeperLeagueId, cancellationToken);

        var opponentRosterDoc = allRosterDocs.FirstOrDefault(r =>
            r.SleeperRosterId == opponentEntry.RosterId.ToString());

        var opponentTeamName = opponentRosterDoc?.TeamName ?? "Opponent";
        var opponentOwnerName = opponentRosterDoc?.OwnerName ?? "Unknown";

        // 6 — Collect all player IDs from both sides
        var myPlayerIds = myRosterDoc.PlayerIds;
        var opponentPlayerIds = opponentRosterDoc?.PlayerIds ?? [];
        var allPlayerIds = myPlayerIds.Concat(opponentPlayerIds).Distinct().ToList();

        // 7 — Bulk load player details + sim results + injuries
        var players = await playerRepository.GetBySleeperIdsAsync(allPlayerIds, cancellationToken);
        var playerLookup = players.ToDictionary(p => p.SleeperPlayerId!, p => p);

        var simDocs = await simulationRepository.GetLatestBySleeperIdsAsync(
            allPlayerIds, request.Season, cancellationToken);
        var simLookup = simDocs.ToDictionary(s => s.SleeperPlayerId ?? string.Empty, s => s);

        var injuryDocs = await injuryAlertRepository.GetActiveAlertsAsync(null, cancellationToken);
        var injuryLookup = injuryDocs
            .Where(i => i.SleeperPlayerId != null)
            .GroupBy(i => i.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.First());

        // 8 — Build starters sets from Sleeper matchup entry
        var myStarterSet = (myMatchupEntry.Starters ?? []).ToHashSet();
        var oppStarterSet = (opponentEntry.Starters ?? []).ToHashSet();

        // 9 — Assemble both sides
        var mySide = BuildSide(
            myRosterDoc.TeamName, myRosterDoc.OwnerName,
            myPlayerIds, myStarterSet,
            playerLookup, simLookup, injuryLookup);

        var oppSide = BuildSide(
            opponentTeamName, opponentOwnerName,
            opponentPlayerIds, oppStarterSet,
            playerLookup, simLookup, injuryLookup);

        // 10 — Win probability from projected totals
        var (myWinProb, oppWinProb) = CalculateWinProbability(
            mySide.TotalProjectedPoints, oppSide.TotalProjectedPoints,
            mySide.ProjectedFloor, oppSide.ProjectedFloor,
            mySide.ProjectedCeiling, oppSide.ProjectedCeiling);

        return new MyMatchupDto(
            Week: request.Week,
            Season: request.Season,
            MyTeam: mySide,
            Opponent: oppSide,
            MyWinProbability: myWinProb,
            OpponentWinProbability: oppWinProb);
    }

    private static MyMatchupSideDto BuildSide(
        string teamName, string ownerName,
        IEnumerable<string> playerIds,
        HashSet<string> starterSet,
        Dictionary<string, FF.Domain.Entities.Player> playerLookup,
        Dictionary<string, FF.Domain.Documents.SimulationResultDocument> simLookup,
        Dictionary<string, FF.Domain.Documents.InjuryAlertDocument> injuryLookup)
    {
        var matchupPlayers = playerIds.Select(sleeperPlayerId =>
        {
            playerLookup.TryGetValue(sleeperPlayerId, out var player);
            simLookup.TryGetValue(sleeperPlayerId, out var sim);
            injuryLookup.TryGetValue(sleeperPlayerId, out var injury);

            return new MyMatchupPlayerDto(
                SleeperPlayerId: sleeperPlayerId,
                PlayerName: player?.FullName ?? "Unknown Player",
                Position: player?.Position.ToString() ?? "?",
                NflTeam: player?.NflTeam ?? "—",
                IsStarter: starterSet.Contains(sleeperPlayerId),
                MedianProjectedPoints: sim is not null ? (double)sim.Median : null,
                FloorProjectedPoints: sim is not null ? (double)sim.Floor : null,
                CeilingProjectedPoints: sim is not null ? (double)sim.Ceiling : null,
                InjuryDesignation: injury?.Designation);
        })
        .OrderByDescending(p => p.IsStarter)
        .ThenBy(p => PositionOrder(p.Position))
        .ThenBy(p => p.PlayerName)
        .ToList();

        // Sum starters only for projected totals
        var starters = matchupPlayers.Where(p => p.IsStarter).ToList();
        var totalMedian = starters.Sum(p => p.MedianProjectedPoints ?? 0);
        var totalFloor = starters.Sum(p => p.FloorProjectedPoints ?? 0);
        var totalCeiling = starters.Sum(p => p.CeilingProjectedPoints ?? 0);

        return new MyMatchupSideDto(
            TeamName: teamName,
            OwnerName: ownerName,
            TotalProjectedPoints: totalMedian,
            ProjectedFloor: totalFloor,
            ProjectedCeiling: totalCeiling,
            Players: matchupPlayers);
    }

    /// <summary>
    /// Estimates win probability using a logistic function on the projected point spread.
    /// A 10-point spread ≈ 75% win probability, which is reasonable for fantasy.
    /// </summary>
    private static (double myWinProb, double oppWinProb) CalculateWinProbability(
        double myMedian, double oppMedian,
        double myFloor, double oppFloor,
        double myCeiling, double oppCeiling)
    {
        // Blended score: weight median heavily, small adjustment for range overlap
        var spread = myMedian - oppMedian;
        var myRange = myCeiling - myFloor;
        var oppRange = oppCeiling - oppFloor;
        var avgRange = (myRange + oppRange) / 2.0;

        // k controls steepness: at k=0.1, a 10pt spread → ~73% win prob
        const double k = 0.10;
        var adjustedSpread = avgRange > 0 ? spread / (1 + 0.05 * avgRange) : spread;
        var myWinProb = 1.0 / (1.0 + Math.Exp(-k * adjustedSpread));
        var oppWinProb = 1.0 - myWinProb;

        return (Math.Round(myWinProb, 3), Math.Round(oppWinProb, 3));
    }

    private static int PositionOrder(string position) => position switch
    {
        "QB" => 0,
        "RB" => 1,
        "WR" => 2,
        "TE" => 3,
        "K" => 4,
        _ => 5
    };
}