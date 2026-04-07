using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Team.Queries;

public class GetMyRosterQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IInjuryAlertRepository injuryAlertRepository)
    : IRequestHandler<GetMyRosterQuery, MyRosterDto?>
{
    public async Task<MyRosterDto?> Handle(
        GetMyRosterQuery request, CancellationToken cancellationToken)
    {
        // 1 — Find user's roster document
        var rosterDoc = await rosterPlayerRepository.GetBySleeperUserIdAsync(
            request.SleeperUserId, request.SleeperLeagueId, cancellationToken);

        if (rosterDoc is null) return null;

        var playerIds = rosterDoc.PlayerIds;
        if (playerIds.Count == 0) return BuildEmptyRoster(rosterDoc);

        // 2 — Load player details from PostgreSQL (bulk)
        var players = await playerRepository.GetBySleeperIdsAsync(playerIds, cancellationToken);
        var playerLookup = players.ToDictionary(p => p.SleeperPlayerId!, p => p);

        // 3 — Load latest simulation results for projected points
        var simDocs = await simulationRepository.GetLatestBySleeperIdsAsync(
            playerIds, DateTime.UtcNow.Year, cancellationToken);
        var simLookup = simDocs.ToDictionary(s => s.SleeperPlayerId ?? string.Empty, s => s);

        // 4 — Load injury alerts
        var injuryDocs = await injuryAlertRepository.GetActiveAlertsAsync(null, cancellationToken);
        var injuryLookup = injuryDocs
            .Where(i => i.SleeperPlayerId != null)
            .GroupBy(i => i.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.First());

        // 5 — Assemble DTO
        var starterSet = rosterDoc.StarterIds.ToHashSet();

        var rosterPlayers = playerIds
            .Select(sleeperPlayerId =>
            {
                playerLookup.TryGetValue(sleeperPlayerId, out var player);
                simLookup.TryGetValue(sleeperPlayerId, out var sim);
                injuryLookup.TryGetValue(sleeperPlayerId, out var injury);

                return new MyRosterPlayerDto(
                    SleeperPlayerId: sleeperPlayerId,
                    PlayerName: player?.FullName ?? "Unknown Player",
                    Position: player?.Position.ToString() ?? "?",
                    NflTeam: player?.NflTeam ?? "—",
                    Age: player?.Age,
                    InjuryDesignation: injury?.Designation,
                    IsStarter: starterSet.Contains(sleeperPlayerId),
                    IsOnIr: rosterDoc.StarterIds.Contains(sleeperPlayerId) is false
                             && player?.InjuryStatus == "IR",
                    MedianProjectedPoints: sim is not null ? (double)sim.Median : null,
                    ByeWeek: null); // TEAM-001B — bye weeks not yet synced
            })
            .OrderBy(p => PositionOrder(p.Position))
            .ThenBy(p => p.PlayerName)
            .ToList();

        return new MyRosterDto(
            TeamName: rosterDoc.TeamName,
            OwnerName: rosterDoc.OwnerName,
            LeagueId: request.SleeperLeagueId,
            Wins: 0,   // TEAM-001B — wire from Roster entity
            Losses: 0,
            WaiverPosition: 0,
            Players: rosterPlayers);
    }

    private static MyRosterDto BuildEmptyRoster(RosterPlayerDocument doc) =>
        new(doc.TeamName, doc.OwnerName, doc.SleeperLeagueId, 0, 0, 0, []);

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