using Refit;

namespace FF.Infrastructure.ExternalServices.OddsAPI;

public interface IOddsApiClient
{
    [Get("/v4/sports/americanfootball_nfl/odds")]
    Task<List<OddsApiGame>> GetNflOddsAsync(
        [AliasAs("apiKey")] string apiKey,
        [AliasAs("regions")] string regions = "us",
        [AliasAs("markets")] string markets = "spreads,totals",
        [AliasAs("oddsFormat")] string oddsFormat = "american",
        CancellationToken ct = default);
}