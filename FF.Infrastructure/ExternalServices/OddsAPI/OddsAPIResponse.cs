using System.Text.Json.Serialization;

namespace FF.Infrastructure.ExternalServices.OddsAPI;

public record OddsApiGame(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("commence_time")] DateTime CommenceTime,
    [property: JsonPropertyName("home_team")] string HomeTeam,
    [property: JsonPropertyName("away_team")] string AwayTeam,
    [property: JsonPropertyName("bookmakers")] List<OddsApiBookmaker> Bookmakers);

public record OddsApiBookmaker(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("markets")] List<OddsApiMarket> Markets);

public record OddsApiMarket(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("outcomes")] List<OddsApiOutcome> Outcomes);

public record OddsApiOutcome(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("point")] decimal? Point);