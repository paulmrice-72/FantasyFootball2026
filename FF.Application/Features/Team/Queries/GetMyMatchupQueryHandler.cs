// FF.Application/Features/Team/Queries/GetMyMatchupQueryHandler.cs
using FF.Application.Interfaces.External;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

public class GetMyMatchupQueryHandler(
    ISleeperMatchupService sleeperMatchupService,
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IInjuryAlertRepository injuryAlertRepository,
    ILeagueRepository leagueRepository,
    ILeagueContextResolverService leagueContextResolver,
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

        // 1 — Resolve league context (scoring format, roster config)
        var league = await leagueRepository.GetBySleeperIdAsync(
            request.SleeperLeagueId, request.Season, cancellationToken);

        LeagueContext? leagueCtx = null;
        if (league is not null)
            leagueCtx = await leagueContextResolver.ResolveAsync(league.Id, cancellationToken);

        var scoringFormatLabel = leagueCtx?.ScoringFormat.ToString() ?? "HalfPpr";
        var rosterConfig = leagueCtx?.RosterConfig;

        // 2 — Get all matchups for this week from Sleeper
        var matchups = await sleeperMatchupService.GetMatchupsAsync(
            request.SleeperLeagueId, request.Week, cancellationToken);

        if (matchups.Count == 0)
        {
            logger.LogWarning("No matchup data from Sleeper for league {LeagueId} week {Week}",
                request.SleeperLeagueId, request.Week);
            return null;
        }

        // 3 — Find the user's roster document
        var myRosterDoc = await rosterPlayerRepository.GetBySleeperUserIdAsync(
            request.SleeperUserId, request.SleeperLeagueId, cancellationToken);

        if (myRosterDoc is null)
        {
            logger.LogWarning("Roster not found for user {SleeperUserId}", request.SleeperUserId);
            return null;
        }

        // 4 — Match to Sleeper matchup entry
        var myMatchupEntry = matchups.FirstOrDefault(
            m => m.RosterId.ToString() == myRosterDoc.SleeperRosterId);

        if (myMatchupEntry is null)
        {
            logger.LogWarning("No matchup entry found for roster {RosterId}",
                myRosterDoc.SleeperRosterId);
            return null;
        }

        // 5 — Find opponent
        var opponentEntry = matchups.FirstOrDefault(
            m => m.MatchupId == myMatchupEntry.MatchupId
              && m.RosterId != myMatchupEntry.RosterId);

        if (opponentEntry is null)
        {
            logger.LogWarning("No opponent found for matchup {MatchupId}", myMatchupEntry.MatchupId);
            return null;
        }

        // 6 — Get opponent roster doc
        var allRosterDocs = await rosterPlayerRepository.GetByLeagueAsync(
            request.SleeperLeagueId, cancellationToken);

        var opponentRosterDoc = allRosterDocs.FirstOrDefault(
            r => r.SleeperRosterId == opponentEntry.RosterId.ToString());

        var opponentTeamName = opponentRosterDoc?.TeamName ?? "Opponent";
        var opponentOwnerName = opponentRosterDoc?.OwnerName ?? "Unknown";

        // 7 — Collect all player IDs from both sides
        var myPlayerIds = myRosterDoc.PlayerIds;
        var opponentPlayerIds = opponentRosterDoc?.PlayerIds ?? [];
        var allPlayerIds = myPlayerIds.Concat(opponentPlayerIds).Distinct().ToList();

        // 8 — Bulk load player details, sim results, injuries
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

        // 9 — Build starter sets — filter Sleeper's "0" empty-slot placeholders
        var myStarterSet = (myMatchupEntry.Starters ?? [])
            .Where(s => s != "0").ToHashSet();
        var oppStarterSet = (opponentEntry.Starters ?? [])
            .Where(s => s != "0").ToHashSet();

        // 10 — Assemble both sides
        var mySide = BuildSide(
            myRosterDoc.TeamName,
            myRosterDoc.OwnerName,
            myRosterDoc.SleeperRosterId,
            myPlayerIds,
            myStarterSet,
            playerLookup,
            simLookup,
            injuryLookup,
            league?.Id,
            scoringFormatLabel);

        var oppSide = BuildSide(
            opponentTeamName,
            opponentOwnerName,
            opponentRosterDoc?.SleeperRosterId,
            opponentPlayerIds,
            oppStarterSet,
            playerLookup,
            simLookup,
            injuryLookup,
            league?.Id,
            scoringFormatLabel);

        // 11 — Win probability
        var (myWinProb, oppWinProb) = CalculateWinProbability(
            mySide.TotalProjectedPoints,
            oppSide.TotalProjectedPoints,
            mySide.ProjectedFloor,
            oppSide.ProjectedFloor,
            mySide.ProjectedCeiling,
            oppSide.ProjectedCeiling);

        return new MyMatchupDto(
            Week: request.Week,
            Season: request.Season,
            ScoringFormat: scoringFormatLabel,
            MyTeam: mySide,
            Opponent: oppSide,
            MyWinProbability: myWinProb,
            OpponentWinProbability: oppWinProb);
    }

    private static MyMatchupSideDto BuildSide(
        string teamName,
        string ownerName,
        string? rosterId,
        IEnumerable<string> playerIds,
        HashSet<string> starterSet,
        Dictionary<string, FF.Domain.Entities.Player> playerLookup,
        Dictionary<string, FF.Domain.Documents.SimulationResultDocument> simLookup,
        Dictionary<string, FF.Domain.Documents.InjuryAlertDocument> injuryLookup,
        Guid? leagueId,
        string scoringFormat)
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
                SlotLabel: starterSet.Contains(sleeperPlayerId) ? "STR" : "BN",
                MedianProjectedPoints: sim is not null ? (double)sim.Median : null,
                FloorProjectedPoints: sim is not null ? (double)sim.Floor : null,
                CeilingProjectedPoints: sim is not null ? (double)sim.Ceiling : null,
                InjuryDesignation: injury?.Designation,
                LeagueId: leagueId,
                ScoringFormat: scoringFormat);
        })
        .OrderByDescending(p => p.IsStarter)
        .ThenBy(p => PositionOrder(p.Position))
        .ThenBy(p => p.PlayerName)
        .ToList();

        var starters = matchupPlayers.Where(p => p.IsStarter).ToList();

        // Pooled floor/ceiling: centre on median sum ± pooled std dev
        // Each player's std dev ≈ (ceiling - floor) / 4 (normal approximation)
        var totalMedian = starters.Sum(p => p.MedianProjectedPoints ?? 0);
        var pooledVariance = starters.Sum(p =>
        {
            var spread = (p.CeilingProjectedPoints ?? 0) - (p.FloorProjectedPoints ?? 0);
            var stdDev = spread / 4.0;
            return stdDev * stdDev;
        });
        var pooledStdDev = Math.Sqrt(pooledVariance);

        var teamFloor = totalMedian - pooledStdDev;
        var teamCeiling = totalMedian + pooledStdDev;

        return new MyMatchupSideDto(
            TeamName: teamName,
            OwnerName: ownerName,
            SleeperRosterId: rosterId,
            TotalProjectedPoints: totalMedian,
            ProjectedFloor: teamFloor,
            ProjectedCeiling: teamCeiling,
            Players: matchupPlayers);
    }

    private static (double myWinProb, double oppWinProb) CalculateWinProbability(
        double myMedian, double oppMedian,
        double myFloor, double oppFloor,
        double myCeiling, double oppCeiling)
    {
        var spread = myMedian - oppMedian;
        var myRange = myCeiling - myFloor;
        var oppRange = oppCeiling - oppFloor;
        var avgRange = (myRange + oppRange) / 2.0;

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