using FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using MediatR;

namespace FF.Application.Features.WaiverRecommendations.Queries;

public class GetOffSeasonAvailablePlayersQueryHandler(
    IDynastyValuationRepository dynastyRepository,
    IRosterPlayerRepository rosterPlayerRepository)
    : IRequestHandler<GetOffSeasonAvailablePlayersQuery, IReadOnlyList<OffSeasonAvailablePlayerDto>>
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
            .Select((v, i) => new OffSeasonAvailablePlayerDto(
                SleeperPlayerId: v.SleeperPlayerId,
                PlayerName: v.PlayerName,
                Position: v.Position,
                NflTeam: v.NflTeam,
                Age: v.Age,
                TradeValue: v.TradeValue,
                Rank: i + 1))
            .ToList();

        return available;
    }
}