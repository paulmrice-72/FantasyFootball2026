namespace FF.Application.Players.Queries.GetAllPlayers;

public record GetAllPlayersResponse(
    IReadOnlyList<PlayerDto> Players,
    int TotalCount);

public record PlayerDto(
    Guid Id,
    string FullName,
    string Position,
    string? NflTeam,
    string Status);