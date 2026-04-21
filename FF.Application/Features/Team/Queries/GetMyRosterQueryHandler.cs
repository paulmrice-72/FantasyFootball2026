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
        var taxiSet = rosterDoc.TaxiIds.ToHashSet();
        var irSet = rosterDoc.IrIds.ToHashSet();    // ← add this

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
IsOnIr: irSet.Contains(sleeperPlayerId),    // ← was the broken line
IsOnTaxi: taxiSet.Contains(sleeperPlayerId),
                    MedianProjectedPoints: sim is not null ? (double)sim.Median : null,
                    ByeWeek: player is not null ? GetByeWeek(player.NflTeam) : null);
            })
            .OrderBy(p => PositionOrder(p.Position))
            .ThenBy(p => RoleOrder(p))
            .ThenBy(p => p.PlayerName)
            .ToList();

            return new MyRosterDto(
                TeamName: rosterDoc.TeamName,
                OwnerName: rosterDoc.OwnerName,
                OwnerAvatar: rosterDoc.OwnerAvatar,    // ← NEW
                LeagueId: request.SleeperLeagueId,
                Wins: rosterDoc.Wins,
                Losses: rosterDoc.Losses,
                WaiverPosition: rosterDoc.WaiverPosition,
                Players: rosterPlayers);
    }

    private static MyRosterDto BuildEmptyRoster(RosterPlayerDocument doc) =>
        new(doc.TeamName, doc.OwnerName, doc.OwnerAvatar, doc.SleeperLeagueId, 0, 0, 0, []);

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
        return 1; // bench
    }
    /// <summary>
    /// 2025 NFL bye weeks by team. Update each September.
    /// </summary>
    private static string? GetByeWeek(string? nflTeam) =>
        nflTeam is null ? null : ByeWeeks2025.TryGetValue(nflTeam, out var week) ? $"Wk {week}" : null;

    private static readonly Dictionary<string, int> ByeWeeks2025 = new()
    {
        ["ARI"] = 11,
        ["ATL"] = 12,
        ["BAL"] = 14,
        ["BUF"] = 12,
        ["CAR"] = 11,
        ["CHI"] = 7,
        ["CIN"] = 12,
        ["CLE"] = 10,
        ["DAL"] = 7,
        ["DEN"] = 14,
        ["DET"] = 5,
        ["GB"] = 6,
        ["HOU"] = 14,
        ["IND"] = 12,
        ["JAX"] = 12,
        ["KC"] = 6,
        ["LAC"] = 5,
        ["LAR"] = 6,
        ["LV"] = 10,
        ["MIA"] = 6,
        ["MIN"] = 6,
        ["NE"] = 14,
        ["NO"] = 12,
        ["NYG"] = 11,
        ["NYJ"] = 12,
        ["PHI"] = 5,
        ["PIT"] = 9,
        ["SEA"] = 10,
        ["SF"] = 9,
        ["TB"] = 11,
        ["TEN"] = 5,
        ["WAS"] = 14
    };
}