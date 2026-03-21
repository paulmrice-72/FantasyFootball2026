using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.ExternalServices.OddsAPI;
using FF.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FF.Infrastructure.Jobs;

public class VegasLineSyncJob(
    IOddsApiClient oddsApiClient,
    IVegasLineRepository vegasLineRepository,
    IOptions<OddsApiSettings> settings,
    ILogger<VegasLineSyncJob> logger)
{
    // Preferred bookmaker priority for spread selection
    private static readonly string[] BookmakerPriority =
        ["draftkings", "fanduel", "betmgm", "caesars", "unibet"];

    public async Task RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("VegasLineSyncJob started");

        List<OddsApiGame> games;
        try
        {
            games = await oddsApiClient.GetNflOddsAsync(settings.Value.ApiKey, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch odds from The Odds API");
            return;
        }

        if (games.Count == 0)
        {
            logger.LogWarning("OddsAPI returned 0 NFL games — off-season or no lines posted yet");
            return;
        }

        var season = GetCurrentNflSeason();
        var nflWeek = GetCurrentNflWeek();
        var docs = new List<VegasLineDocument>();

        foreach (var game in games)
        {
            var bookmaker = SelectBookmaker(game);
            if (bookmaker is null)
            {
                logger.LogDebug("No usable bookmaker for {Home} vs {Away}", game.HomeTeam, game.AwayTeam);
                continue;
            }

            var spreadsMarket = bookmaker.Markets.FirstOrDefault(m => m.Key == "spreads");
            var totalsMarket = bookmaker.Markets.FirstOrDefault(m => m.Key == "totals");

            var homeOutcome = spreadsMarket?.Outcomes.FirstOrDefault(o =>
                o.Name.Equals(game.HomeTeam, StringComparison.OrdinalIgnoreCase));
            var overOutcome = totalsMarket?.Outcomes.FirstOrDefault(o =>
                o.Name.Equals("Over", StringComparison.OrdinalIgnoreCase));

            if (homeOutcome?.Point is null) continue;

            var homeSpread = homeOutcome.Point.Value;

            // Normalize team abbreviations — The Odds API uses full names; we need abbreviations
            var homeAbbr = TeamNameResolver.Resolve(game.HomeTeam);
            var awayAbbr = TeamNameResolver.Resolve(game.AwayTeam);

            if (homeAbbr is null || awayAbbr is null)
            {
                logger.LogWarning("Could not resolve team abbreviation for {Home} or {Away}",
                    game.HomeTeam, game.AwayTeam);
                continue;
            }

            docs.Add(new VegasLineDocument
            {
                Season = season,
                Week = nflWeek,
                HomeTeam = homeAbbr,
                AwayTeam = awayAbbr,
                HomeSpread = homeSpread,
                AwaySpread = -homeSpread,
                OverUnder = overOutcome?.Point ?? 0m,
                Bookmaker = bookmaker.Key,
                CommenceTime = game.CommenceTime,
                FetchedAt = DateTime.UtcNow
            });
        }

        await vegasLineRepository.UpsertBatchAsync(docs, ct);
        logger.LogInformation("VegasLineSyncJob complete — {Count} lines upserted", docs.Count);
    }

    private static OddsApiBookmaker? SelectBookmaker(OddsApiGame game)
    {
        foreach (var key in BookmakerPriority)
        {
            var bm = game.Bookmakers.FirstOrDefault(b =>
                b.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (bm is not null) return bm;
        }
        return game.Bookmakers.FirstOrDefault();
    }
    private static int GetCurrentNflSeason()
    {
        var now = DateTime.UtcNow;
        return now.Month >= 3 ? now.Year : now.Year - 1;
    }

    private static int GetCurrentNflWeek()
    {
        var now = DateTime.UtcNow;
        var season = GetCurrentNflSeason();
        var seasonStart = GetSeasonStart(season);
        if (now < seasonStart) return 18;
        var daysSinceStart = (now - seasonStart).TotalDays;
        var week = (int)(daysSinceStart / 7) + 1;
        return Math.Clamp(week, 1, 18);
    }

    private static DateTime GetSeasonStart(int season)
    {
        var sept1 = new DateTime(season, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)sept1.DayOfWeek + 7) % 7;
        return sept1.AddDays(daysUntilThursday);
    }
}