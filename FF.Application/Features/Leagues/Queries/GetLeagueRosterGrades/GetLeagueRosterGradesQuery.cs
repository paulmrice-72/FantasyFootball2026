// FF.Application/Features/Leagues/Queries/GetLeagueRosterGrades/GetLeagueRosterGradesQuery.cs
using MediatR;

namespace FF.Application.Features.Leagues.Queries.GetLeagueRosterGrades;

public record GetLeagueRosterGradesQuery(
    string SleeperLeagueId,
    int Season) : IRequest<LeagueRosterGradesDto?>;

public record LeagueRosterGradesDto(
    string SleeperLeagueId,
    int Season,
    List<TeamRosterGradeDto> Teams);

public record TeamRosterGradeDto(
    string SleeperRosterId,
    string TeamName,
    string OwnerName,
    string DepthGrade,           // A / B / C / D / F
    double DepthScore,           // 0–100 normalised
    string DynastyGrade,         // A / B / C / D / F
    double DynastyScore,         // raw blend value
    string TeamProfile,          // Contender / Win-Now / Transitioning / Rebuilding
    double DraftCapitalScore,    // 0–100 normalised across league
    int OwnedPickCount,          // raw count of future picks owned
    List<TeamAssetDto> TopAssets);

public record TeamAssetDto(
    string PlayerName,
    string Position,
    double TradeValue,
    double BreakoutScore,
    int? Age);