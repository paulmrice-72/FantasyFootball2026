// FF.Infrastructure/ExternalServices/FantasyFootballCalculator/FantasyFootballCalculatorService.cs
using FF.Application.Interfaces.External;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FF.Infrastructure.ExternalServices.FantasyFootballCalculator;

/// <summary>
/// Calls the free FFC ADP REST API and parses the response.
/// Base URL: https://fantasyfootballcalculator.com/api/v1/adp
/// Supports formats: ppr, half-ppr, standard, 2qb
/// </summary>
public class FantasyFootballCalculatorService(
    HttpClient httpClient,
    ILogger<FantasyFootballCalculatorService> logger)
    : IFantasyFootballCalculatorService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IReadOnlyList<FfcPlayerAdp>> GetAdpAsync(
        int season,
        string scoringFormat = "ppr",
        int teamCount = 12,
        CancellationToken ct = default)
    {
        var url = $"api/v1/adp/{scoringFormat}?teams={teamCount}&year={season}";

        logger.LogInformation(
            "Fetching FFC ADP: format={Format} teams={Teams} season={Season}",
            scoringFormat, teamCount, season);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FFC API request failed for {Url}", url);
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("FFC API returned {Status} for {Url}",
                (int)response.StatusCode, url);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(ct);

        FfcApiResponse? apiResponse;
        try
        {
            apiResponse = JsonSerializer.Deserialize<FfcApiResponse>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse FFC API response");
            return [];
        }

        if (apiResponse?.Players is null)
        {
            logger.LogWarning("FFC API response contained no players");
            return [];
        }

        var result = apiResponse.Players
            .Where(p => !string.IsNullOrEmpty(p.Name) && p.Adp > 0)
            .Select(p => new FfcPlayerAdp(
                Name: p.Name!,
                Position: NormalizePosition(p.Position),
                Team: string.IsNullOrEmpty(p.Team) ? null : p.Team,
                Adp: p.Adp,
                AdpRound: (int)Math.Ceiling(p.Adp / teamCount),
                PickCount: p.TimesSelected))
            .ToList();

        logger.LogInformation("FFC ADP loaded: {Count} players for season {Season}",
            result.Count, season);

        return result;
    }

    private static string NormalizePosition(string? pos) => pos?.ToUpperInvariant() switch
    {
        "QB" => "QB",
        "RB" => "RB",
        "WR" => "WR",
        "TE" => "TE",
        // 2026-09-07: "PK" was missing and fell through to the default arm, so
        // every kicker kept the token "PK" and matched nothing downstream —
        // 0 of 21 in the first live run, against 192 kickers sitting available
        // in the Players table. FFC publishes place kickers as "PK"; Sleeper and
        // FF.Domain.Enums.Position both say "K". Normalise to "K".
        "K" or "PK" => "K",
        // 2026-09-07: was "DST". Sleeper — which supplies every OTHER position
        // string in this system, including the ones on live draft picks — calls a
        // team defense "DEF", and so does FF.Domain.Enums.Position. Emitting a
        // third spelling here meant an ADP row and a drafted-player row for the
        // same defense compared unequal, so a defense could never be matched,
        // counted, or marked as drafted. One vocabulary.
        "D" or "DST" or "DEF" => "DEF",
        _ => pos?.ToUpperInvariant() ?? "?"
    };

    // ── FFC API shapes ────────────────────────────────────────────────────────

    private class FfcApiResponse
    {
        [JsonPropertyName("players")]
        public List<FfcApiPlayer>? Players { get; set; }
    }

    private class FfcApiPlayer
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("position")]
        public string? Position { get; set; }

        [JsonPropertyName("team")]
        public string? Team { get; set; }

        [JsonPropertyName("adp")]
        public double Adp { get; set; }

        [JsonPropertyName("times_selected")]
        public int TimesSelected { get; set; }
    }
}