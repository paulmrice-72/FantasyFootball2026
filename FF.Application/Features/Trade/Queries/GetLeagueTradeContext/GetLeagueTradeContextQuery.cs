// FF.Application/Features/Trade/Queries/GetLeagueTradeContext/GetLeagueTradeContextQuery.cs
using MediatR;

namespace FF.Application.Features.Trade.Queries.GetLeagueTradeContext;

public record GetLeagueTradeContextQuery(
    string LeagueId,
    string SleeperUserId,
    int Season) : IRequest<LeagueTradeContextDto>;
