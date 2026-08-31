namespace FF.Application.Features.WaiverRecommendations.Queries.OffSeasonAvailablePlayer;

// FAN-112 (2026-08-30): TradeValue renamed to Value + ValueLabel added.
// This off-season fallback now ranks by Dynasty Trade Value for dynasty
// leagues but by Redraft ADP for redraft leagues (dynasty trade value has
// no meaning in a one-year league) — the two are different units, so the
// DTO carries a label the UI displays instead of a hardcoded "Dynasty Value"
// column header.
public record OffSeasonAvailablePlayerDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string? NflTeam,
    int Age,
    double Value,
    string ValueLabel,
    int Rank,
    string? CollegeTeam);
