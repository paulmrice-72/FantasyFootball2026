// FF.Application/Players/Queries/GetRookiePool/RookiePlayerDto.cs
namespace FF.Application.Players.Queries.GetRookiePool;

public record RookiePlayerDto(
    string SleeperPlayerId,
    string FullName,
    string Position,
    string? NflTeam,
    int? Age,
    int? DraftRound,
    int? DraftPick,
    string? CollegeTeam,
    string? HeadshotUrl,

    // From dynasty_valuations
    double? CareerValueScore,
    double? TradeValue,
    double? DiscountedFutureValue,
    double? BreakoutScore,

    // From fantasyPros_rookie_rankings
    int? FantasyProsRank,
    int? FantasyProsPositionRank,
    string? FantasyProsTier,

    // ── E10 Composite score ───────────────────────────────────────────────
    double DynastyScore,
    double DraftCapitalScore,
    double PositionalScore,
    double ValuationBlendScore,
    double FantasyProsScore
);