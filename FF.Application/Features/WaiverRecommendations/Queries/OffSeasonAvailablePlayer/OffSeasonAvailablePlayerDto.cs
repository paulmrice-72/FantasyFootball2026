namespace FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;

public record OffSeasonAvailablePlayerDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string? NflTeam,
    int Age,
    double TradeValue,
    int Rank,
    string? CollegeTeam);