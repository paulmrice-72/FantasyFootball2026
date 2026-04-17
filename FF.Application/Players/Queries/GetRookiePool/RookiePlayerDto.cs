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

    // From pff_draft_grades
    double? PffGrade,
    int? PffRank,

    // From consensus_adp
    double? ConsensusAdp,
    int? ConsensusAdpRank,
    string? AdpSource,

    // Composite score + per-signal breakdown
    double DynastyScore,
    double DraftCapitalScore,
    double FantasyProsScore,
    double PffGradeScore,
    double ConsensusAdpScore,
    double ValuationBlendScore,
    double PositionalScore,
    List<string> ActiveSignals
);