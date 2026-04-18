using MediatR;

namespace FF.Application.Features.Trade.Queries.GetRedraftTradeValue;

public record GetRedraftTradeValueQuery(List<string> SleeperPlayerIds)
    : IRequest<List<RedraftTradeValueDto>>;

public record RedraftTradeValueDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string Team,
    double MedianProjectedPoints,
    string ValueLabel);