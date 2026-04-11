using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using MediatR;

namespace FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;

public class GetOffSeasonAvailablePlayersQueryHandler(
    IDynastyValuationRepository dynastyRepository,
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository)
{
    public async Task<IReadOnlyList<OffSeasonAvailablePlayerDto>> Handle(
        GetOffSeasonAvailablePlayersQuery request,
        CancellationToken cancellationToken)
    {
        // 1 — Load rostered Sleeper IDs for this league
        var leagueRosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.LeagueId, cancellationToken);

        var rosteredIds = leagueRosters
            .SelectMany(r => r.PlayerIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2 — Fetch a generous buffer of top dynasty valuations
        var valuations = await dynastyRepository
            .GetTopByTradeValueAsync(200, request.Position, cancellationToken);

        // 3 — Exclude rostered players, apply top N
        var available = valuations
                    .Where(v => !string.IsNullOrEmpty(v.SleeperPlayerId)
                                && !rosteredIds.Contains(v.SleeperPlayerId))
                    .Take(request.Top)
                    .ToList();

        // Bulk load college data from SQL Players
        var availableIds = available.Select(v => v.SleeperPlayerId).ToList();
        var players = await playerRepository.GetBySleeperIdsAsync(availableIds, cancellationToken);
        var collegeLookup = players
            .Where(p => p.SleeperPlayerId != null && p.CollegeTeam != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p.CollegeTeam);

        return available
            .Select((v, i) => new OffSeasonAvailablePlayerDto(
                SleeperPlayerId: v.SleeperPlayerId,
                PlayerName: v.PlayerName,
                Position: v.Position,
                NflTeam: v.NflTeam,
                Age: v.Age,
                TradeValue: v.TradeValue,
                Rank: i + 1,
                CollegeTeam: collegeLookup.TryGetValue(v.SleeperPlayerId, out var college)
                    ? college : null))
            .ToList();
    }
}