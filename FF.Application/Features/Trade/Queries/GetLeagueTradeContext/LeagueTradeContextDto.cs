// FF.Application/Features/Trade/Queries/GetLeagueTradeContext/LeagueTradeContextDto.cs
namespace FF.Application.Features.Trade.Queries.GetLeagueTradeContext;

/// <summary>
/// Full league context returned to the League Trade Analyzer page.
/// Contains my roster, all opponent rosters, and league-wide value rankings.
/// </summary>
public record LeagueTradeContextDto(
    LeagueTeamDto MyTeam,
    List<LeagueTeamDto> Opponents,
    List<LeagueRankingDto> LeagueRankings,
    int DraftRounds);

public record LeagueTeamDto(
    string RosterId,
    string TeamName,
    string OwnerSleeperUserId,
    List<LeaguePlayerDto> Players,
    List<LeaguePickDto> Picks,
    double TotalTradeValue);

public record LeaguePlayerDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string? NflTeam,
    int Age,
    double TradeValue);

public record LeaguePickDto(
    int Season,
    int Round,
    string OriginalTeamName,
    string CurrentTeamName,
    string Description,
    double EstimatedValue,
    int? Slot = null);  // ← add this

public record LeagueRankingDto(
    int Rank,
    string TeamName,
    string OwnerSleeperUserId,
    double TotalTradeValue,
    bool IsMyTeam);
