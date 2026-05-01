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
    IPlayerProjectionRepository projectionRepository,
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

        // 1 — Resolve league context
        var league = await leagueRepository.GetBySleeperIdAsync(
            request.SleeperLeagueId, request.Season, cancellationToken);
        LeagueContext? leagueCtx = null;
        if (league is not null)
            leagueCtx = await leagueContextResolver.ResolveAsync(league.Id, cancellationToken);
        var scoringFormatLabel = leagueCtx?.ScoringFormat.ToString() ?? "HalfPpr";

        // 2 — Sleeper matchups
        var matchups = await sleeperMatchupService.GetMatchupsAsync(
            request.SleeperLeagueId, request.Week, cancellationToken);
        if (matchups.Count == 0)
        {
            logger.LogWarning("No matchup data from Sleeper for league {LeagueId} week {Week}",
                request.SleeperLeagueId, request.Week);
            return null;
        }

        // 3 — My roster doc (for team name / owner name / roster ID)
        var myRosterDoc = await rosterPlayerRepository.GetBySleeperUserIdAsync(
            request.SleeperUserId, request.SleeperLeagueId, cancellationToken);
        if (myRosterDoc is null)
        {
            logger.LogWarning("Roster not found for user {SleeperUserId}", request.SleeperUserId);
            return null;
        }

        // 4 — My matchup entry
        var myMatchupEntry = matchups.FirstOrDefault(
            m => m.RosterId.ToString() == myRosterDoc.SleeperRosterId);
        if (myMatchupEntry is null)
        {
            logger.LogWarning("No matchup entry found for roster {RosterId}", myRosterDoc.SleeperRosterId);
            return null;
        }

        // 5 — Opponent
        var opponentEntry = matchups.FirstOrDefault(
            m => m.MatchupId == myMatchupEntry.MatchupId && m.RosterId != myMatchupEntry.RosterId);
        if (opponentEntry is null)
        {
            logger.LogWarning("No opponent found for matchup {MatchupId}", myMatchupEntry.MatchupId);
            return null;
        }

        // 6 — Opponent roster doc (for team name / owner name)
        var allRosterDocs = await rosterPlayerRepository.GetByLeagueAsync(
            request.SleeperLeagueId, cancellationToken);
        var opponentRosterDoc = allRosterDocs.FirstOrDefault(
            r => r.SleeperRosterId == opponentEntry.RosterId.ToString());
        var opponentTeamName = opponentRosterDoc?.TeamName ?? "Opponent";
        var opponentOwnerName = opponentRosterDoc?.OwnerName ?? "Unknown";

        // 7 — Use MATCHUP Players array as the authoritative roster for this week.
        //     This is more accurate than the roster doc PlayerIds because:
        //       a) It reflects the exact active roster at game time (post-waiver/trade)
        //       b) It includes DEF team abbreviation IDs (e.g. "PHI") that the roster
        //          doc may or may not carry depending on sync timing
        var myPlayerIds = (myMatchupEntry.Players ?? []).Where(id => id != "0").ToList();
        var opponentPlayerIds = (opponentEntry.Players ?? []).Where(id => id != "0").ToList();
        var allPlayerIds = myPlayerIds.Concat(opponentPlayerIds).Distinct().ToList();

        // 8 — Bulk load: players, sims, injuries, projections
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

        var projDocs = await projectionRepository.GetBySleeperIdsAsync(
            allPlayerIds, request.Season, request.Week, cancellationToken);
        var projLookup = projDocs
            .GroupBy(p => p.SleeperPlayerId)
            .ToDictionary(g => g.Key, g => g.First());

        // 9 — Starter sets
        var myStarterSet = (myMatchupEntry.Starters ?? []).Where(s => s != "0").ToHashSet();
        var oppStarterSet = (opponentEntry.Starters ?? []).Where(s => s != "0").ToHashSet();

        // 10 — Build both sides using matchup player lists
        var mySide = BuildSide(
            myRosterDoc.TeamName, myRosterDoc.OwnerName, myRosterDoc.SleeperRosterId,
            myPlayerIds,
            myStarterSet,
            playerLookup, simLookup, injuryLookup, projLookup,
            league?.Id, scoringFormatLabel);

        var oppSide = BuildSide(
            opponentTeamName, opponentOwnerName, opponentRosterDoc?.SleeperRosterId,
            opponentPlayerIds,
            oppStarterSet,
            playerLookup, simLookup, injuryLookup, projLookup,
            league?.Id, scoringFormatLabel);

        // 11 — Win probability
        var (myWinProb, oppWinProb) = CalculateWinProbability(
            mySide.TotalProjectedPoints, oppSide.TotalProjectedPoints,
            mySide.ProjectedFloor, oppSide.ProjectedFloor,
            mySide.ProjectedCeiling, oppSide.ProjectedCeiling);

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
        Dictionary<string, FF.Domain.Documents.PlayerProjectionDocument> projLookup,
        Guid? leagueId,
        string scoringFormat)
    {
        var matchupPlayers = playerIds.Select(sleeperPlayerId =>
        {
            playerLookup.TryGetValue(sleeperPlayerId, out var player);
            simLookup.TryGetValue(sleeperPlayerId, out var sim);
            injuryLookup.TryGetValue(sleeperPlayerId, out var injury);
            projLookup.TryGetValue(sleeperPlayerId, out var proj);

            // DEF entries use team abbreviation IDs (e.g. "PHI", "KC", "DAL").
            // They won't resolve in the player lookup because they're not skill-position
            // players in PostgreSQL. Detect by: no player found + 2-3 uppercase letters + no digits.
            var isDefId = player is null
                          && sleeperPlayerId.Length is >= 2 and <= 3
                          && sleeperPlayerId == sleeperPlayerId.ToUpperInvariant()
                          && sleeperPlayerId.All(char.IsLetter);

            var playerName = player?.FullName
                ?? (isDefId ? $"{sleeperPlayerId} D/ST" : "Unknown Player");
            var position = player?.Position.ToString()
                ?? (isDefId ? "DEF" : "?");
            var nflTeam = player?.NflTeam
                ?? (isDefId ? sleeperPlayerId : "—");

            // Build projection breakdown if available
            ProjectionBreakdownDto? breakdown = proj is null ? null : new ProjectionBreakdownDto(
                ProjectedPoints: (double)(scoringFormat == "Ppr"
                    ? proj.ProjectedPointsPpr
                    : scoringFormat == "Standard"
                        ? proj.ProjectedPoints
                        : proj.ProjectedPointsHalfPpr),
                WeightedAvgPoints: (double)proj.WeightedAvgPoints,
                MatchupAdjustmentFactor: (double)proj.MatchupAdjustmentFactor,
                SnapPctInput: (double)proj.SnapPctInput,
                TargetShareInput: (double)proj.TargetShareInput,
                GameScript: proj.GameScript,
                SpreadInput: (double)proj.SpreadInput,
                ScoringFormat: proj.ScoringFormat,
                Season: proj.Season,
                Week: proj.Week);

            return new MyMatchupPlayerDto(
                SleeperPlayerId: sleeperPlayerId,
                PlayerName: playerName,
                Position: position,
                NflTeam: nflTeam,
                IsStarter: starterSet.Contains(sleeperPlayerId),
                SlotLabel: starterSet.Contains(sleeperPlayerId) ? "STR" : "BN",
                MedianProjectedPoints: sim is not null ? (double)sim.Median : null,
                FloorProjectedPoints: sim is not null ? (double)sim.Floor : null,
                CeilingProjectedPoints: sim is not null ? (double)sim.Ceiling : null,
                InjuryDesignation: injury?.Designation,
                LeagueId: leagueId,
                ScoringFormat: scoringFormat,
                ProjectionBreakdown: breakdown);
        })
        .OrderByDescending(p => p.IsStarter)
        .ThenBy(p => PositionOrder(p.Position))
        .ThenBy(p => p.PlayerName)
        .ToList();

        var starters = matchupPlayers.Where(p => p.IsStarter).ToList();
        var totalMedian = starters.Sum(p => p.MedianProjectedPoints ?? 0);
        var pooledVariance = starters.Sum(p =>
        {
            var spread = (p.CeilingProjectedPoints ?? 0) - (p.FloorProjectedPoints ?? 0);
            var stdDev = spread / 4.0;
            return stdDev * stdDev;
        });
        var pooledStdDev = Math.Sqrt(pooledVariance);

        return new MyMatchupSideDto(
            TeamName: teamName,
            OwnerName: ownerName,
            SleeperRosterId: rosterId,
            TotalProjectedPoints: totalMedian,
            ProjectedFloor: totalMedian - pooledStdDev,
            ProjectedCeiling: totalMedian + pooledStdDev,
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
        return (Math.Round(myWinProb, 3), Math.Round(1.0 - myWinProb, 3));
    }

    private static int PositionOrder(string position) => position switch
    {
        "QB" => 0,
        "RB" => 1,
        "WR" => 2,
        "TE" => 3,
        "K" => 4,
        "DEF" => 5,
        _ => 6
    };
}