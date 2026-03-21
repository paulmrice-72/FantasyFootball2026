namespace FF.Domain.Documents;

/// <summary>
/// Stores the Vegas spread and game total for one NFL matchup per week.
/// One document per game per season per week — upserted on each sync.
/// Convention: Spread is from HomeTeam's perspective.
///   Positive spread = HomeTeam is favourite (e.g. +6.5 means home favoured by 6.5)
///   Negative spread = HomeTeam is underdog
/// Collection: vegas_lines
/// </summary>
public class VegasLineDocument
{
    public string Id { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Week { get; set; }
    public string HomeTeam { get; set; } = string.Empty;   // Sleeper/nflverse abbreviation
    public string AwayTeam { get; set; } = string.Empty;
    public decimal HomeSpread { get; set; }                // + = home favoured
    public decimal AwaySpread { get; set; }                // mirror: AwaySpread = -HomeSpread
    public decimal OverUnder { get; set; }
    public string Bookmaker { get; set; } = string.Empty;  // source book (e.g. "draftkings")
    public DateTime CommenceTime { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}