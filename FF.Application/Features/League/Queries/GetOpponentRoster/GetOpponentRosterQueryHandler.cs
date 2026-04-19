using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Features.League.Queries.GetOpponentRoster;

public class GetOpponentRosterQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IInjuryAlertRepository injuryAlertRepository)
    : IRequestHandler<GetOpponentRosterQuery, MyRosterDto?>
{
    public async Task<MyRosterDto?> Handle(
        GetOpponentRosterQuery request, CancellationToken cancellationToken)
    {
        var rosterDoc = await rosterPlayerRepository.GetByRosterIdAsync(
            request.SleeperRosterId, request.SleeperLeagueId, cancellationToken);

        if (rosterDoc is null) return null;

        var playerIds = rosterDoc.PlayerIds;
        if (playerIds.Count == 0)
            return new MyRosterDto(rosterDoc.TeamName, rosterDoc.OwnerName,
                request.SleeperLeagueId, 0, 0, 0, []);

        var players = await playerRepository.GetBySleeperIdsAsync(playerIds, cancellationToken);
        var simDocs = await simulationRepository.GetLatestBySleeperIdsAsync(
            playerIds, DateTime.UtcNow.Year, cancellationToken);
        var injuryDocs = await injuryAlertRepository.GetActiveAlertsAsync(null, cancellationToken);

        var playerLookup = players.ToDictionary(p => p.SleeperPlayerId!, p => p);
        var simLookup = simDocs.ToDictionary(s => s.SleeperPlayerId ?? string.Empty, s => s);
        var injuryLookup = injuryDocs
            .Where(i => i.SleeperPlayerId != null)
            .GroupBy(i => i.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.First());

        var starterSet = rosterDoc.StarterIds.ToHashSet();
        var taxiSet = rosterDoc.TaxiIds.ToHashSet();
        var irSet = rosterDoc.IrIds.ToHashSet();

        var rosterPlayers = playerIds
            .Select(id =>
            {
                playerLookup.TryGetValue(id, out var player);
                simLookup.TryGetValue(id, out var sim);
                injuryLookup.TryGetValue(id, out var injury);
                return new MyRosterPlayerDto(
                    SleeperPlayerId: id,
                    PlayerName: player?.FullName ?? "Unknown Player",
                    Position: player?.Position.ToString() ?? "?",
                    NflTeam: player?.NflTeam ?? "—",
                    Age: player?.Age,
                    InjuryDesignation: injury?.Designation,
                    IsStarter: starterSet.Contains(id),
                    IsOnIr: irSet.Contains(id),
                    IsOnTaxi: taxiSet.Contains(id),
                    MedianProjectedPoints: sim is not null ? (double)sim.Median : null,
                    ByeWeek: null);
            })
            .OrderBy(p => PositionOrder(p.Position))
            .ThenBy(p => RoleOrder(p))
            .ThenBy(p => p.PlayerName)
            .ToList();

        return new MyRosterDto(
            TeamName: rosterDoc.TeamName,
            OwnerName: rosterDoc.OwnerName,
            LeagueId: request.SleeperLeagueId,
            Wins: rosterDoc.Wins,
            Losses: rosterDoc.Losses,
            WaiverPosition: rosterDoc.WaiverPosition,
            Players: rosterPlayers);
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

    private static int RoleOrder(MyRosterPlayerDto p)
    {
        if (p.IsStarter) return 0;
        if (p.IsOnIr) return 2;
        if (p.IsOnTaxi) return 3;
        return 1;
    }
}