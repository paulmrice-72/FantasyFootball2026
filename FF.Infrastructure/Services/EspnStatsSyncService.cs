// FF.Infrastructure/Services/EspnStatsSyncService.cs
using System.Diagnostics;
using System.Text.Json;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

/// <summary>
/// Replaces the nflverse player_stats_2025.csv pipeline (confirmed 404) with
/// direct ESPN API calls.
///
/// Phase 1 — EspnId bridge:
///   Downloads nflverse players.csv (gsis_id → espn_id mapping) and bulk-updates
///   the Players table via direct SQL — bypasses AsNoTracking limitation.
///
/// Phase 2 — Stats sync:
///   For each Player with an EspnId, fetches ESPN season stats and upserts a
///   Week=0 season-average SimulationResultDocument into simulation_results.
///   CareerSimulationService's multi-season blend reads these naturally.
/// </summary>
public class EspnStatsSyncService(
    FFDbContext db,
    ISimulationResultRepository simulationResultRepository,
    IHttpClientFactory httpClientFactory,
    ILogger<EspnStatsSyncService> logger) : IEspnStatsSyncService
{
    // nflverse players.csv — updated Jan 2026, has gsis_id → espn_id columns
    private const string PlayersCsvUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/players/players.csv";

    // ESPN API — regular season stats (type=2). No auth required.
    private const string EspnStatsUrlTemplate =
        "https://sports.core.api.espn.com/v2/sports/football/leagues/nfl/seasons/{0}/types/2/athletes/{1}/statistics";

    // Half-PPR scoring weights
    private const double PassYdPts = 0.04;
    private const double PassTdPts = 4.0;
    private const double IntPts = -1.0;
    private const double RushYdPts = 0.1;
    private const double RushTdPts = 6.0;
    private const double RecPts = 0.5;
    private const double RecYdPts = 0.1;
    private const double RecTdPts = 6.0;

    // Courtesy delay between ESPN requests to avoid throttling
    private static readonly TimeSpan EspnDelay = TimeSpan.FromMilliseconds(150);

    // ─────────────────────────────────────────────────────────────────────────
    // PHASE 1 — EspnId bridge
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<EspnIdSyncResult> SyncEspnIdsAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("EspnStatsSyncService: Starting EspnId bridge sync from nflverse players.csv");

        // Download nflverse players.csv
        var http = httpClientFactory.CreateClient("NflverseClient");
        string csv;
        try
        {
            csv = await http.GetStringAsync(PlayersCsvUrl, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download nflverse players.csv from {Url}", PlayersCsvUrl);
            throw;
        }

        // Parse gsis_id → espn_id lookup from CSV
        var lookup = ParseGsisToEspnIdLookup(csv);
        logger.LogInformation("EspnId bridge: parsed {Count} gsis→espn_id mappings from players.csv", lookup.Count);

        // Load players that have a GsisId directly from DbContext (tracked not needed —
        // we are using raw SQL update, so AsNoTracking is fine for the read).
        var players = await db.Players
            .Where(p => p.GsisId != null)
            .Select(p => new { p.Id, p.GsisId, p.EspnId })
            .ToListAsync(ct);

        var alreadyHad = players.Count(p => !string.IsNullOrEmpty(p.EspnId));
        var pending = players.Where(p => string.IsNullOrEmpty(p.EspnId)).ToList();

        int matched = 0, skipped = 0;
        var now = DateTime.UtcNow;

        foreach (var player in pending)
        {
            if (lookup.TryGetValue(player.GsisId!, out var espnId))
            {
                // Direct parameterized SQL update — bypasses AsNoTracking entirely
                await db.Database.ExecuteSqlRawAsync(
                    @"UPDATE ""Players"" SET ""EspnId"" = {0}, ""UpdatedAt"" = {1} WHERE ""Id"" = {2}",
                    espnId, now, player.Id);

                matched++;
            }
            else
            {
                skipped++;
            }
        }

        sw.Stop();
        logger.LogInformation(
            "EspnId bridge complete — Matched: {Matched}, Skipped: {Skipped}, AlreadyHad: {Already}, Duration: {Duration:0.0}s",
            matched, skipped, alreadyHad, sw.Elapsed.TotalSeconds);

        return new EspnIdSyncResult(matched, skipped, alreadyHad, sw.Elapsed);
    }

    private static Dictionary<string, string> ParseGsisToEspnIdLookup(string csv)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return lookup;

        var headers = SplitCsvLine(lines[0]);
        int gsisIdx = Array.IndexOf(headers, "gsis_id");
        int espnIdx = Array.IndexOf(headers, "espn_id");

        if (gsisIdx < 0 || espnIdx < 0) return lookup;

        foreach (var line in lines.Skip(1))
        {
            var cols = SplitCsvLine(line);
            if (cols.Length <= Math.Max(gsisIdx, espnIdx)) continue;

            var gsis = cols[gsisIdx].Trim('"').Trim();
            var espn = cols[espnIdx].Trim('"').Trim();

            if (!string.IsNullOrEmpty(gsis) && !string.IsNullOrEmpty(espn))
                lookup[gsis] = espn;
        }

        return lookup;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuote = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"') { inQuote = !inQuote; }
            else if (c == ',' && !inQuote) { result.Add(current.ToString()); current.Clear(); }
            else { current.Append(c); }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PHASE 2 — ESPN stats sync
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<EspnStatsSyncResult> SyncStatsAsync(int season, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("EspnStatsSyncService: Starting ESPN stats sync for season {Season}", season);

        var http = httpClientFactory.CreateClient();

        // Load eligible players directly from DbContext
        var players = await db.Players
            .Where(p => p.EspnId != null && p.SleeperPlayerId != null)
            .Select(p => new
            {
                p.SleeperPlayerId,
                p.EspnId,
                p.FirstName,
                p.LastName,
                p.Position,
                p.NflTeam
            })
            .ToListAsync(ct);

        var noEspnId = await db.Players.CountAsync(p => p.EspnId == null, ct);

        logger.LogInformation("ESPN stats sync: {Count} players have EspnId and will be fetched", players.Count);

        int processed = 0, upserted = 0, failed = 0;
        var documents = new List<SimulationResultDocument>();

        foreach (var player in players)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var url = string.Format(EspnStatsUrlTemplate, season, player.EspnId);
                var json = await http.GetStringAsync(url, ct);
                var stats = ParseEspnStats(json);

                if (stats is null || stats.GamesPlayed <= 0)
                {
                    logger.LogDebug("No {Season} stats for {Player} (EspnId={Id})",
                        season, $"{player.FirstName} {player.LastName}", player.EspnId);
                    processed++;
                    await Task.Delay(EspnDelay, ct);
                    continue;
                }

                var fppg = ComputeFppg(stats);
                var posStr = player.Position.ToString().ToUpper();

                // Week=0 season-average sentinel — same shape CareerSimulationService
                // reads via GetAllSeasonAveragesAsync / multi-season blend logic.
                var doc = new SimulationResultDocument
                {
                    Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    PlayerId = player.SleeperPlayerId!,
                    SleeperPlayerId = player.SleeperPlayerId,
                    PlayerName = $"{player.FirstName} {player.LastName}",
                    Position = posStr,
                    NflTeam = player.NflTeam ?? string.Empty,
                    OpponentTeam = string.Empty,
                    Season = season,
                    Week = 0,
                    Iterations = 0,
                    BaseProjection = (decimal)fppg,
                    StandardDeviation = (decimal)(fppg * 0.30),
                    Floor = (decimal)(fppg * 0.55),
                    Median = (decimal)fppg,
                    Ceiling = (decimal)(fppg * 1.60),
                    Mean = (decimal)fppg,
                    BoomProbability = 0m,
                    BustProbability = 0m,
                    PlayerRole = "SeasonAverage",
                    ScoringFormat = "HalfPpr",
                    CalculatedAt = DateTime.UtcNow,
                    Spread = 0m,
                    GameScript = "ESPN"
                };

                documents.Add(doc);
                upserted++;
                processed++;

                logger.LogDebug("{Player} ({Pos}) — Games: {G}, FPPG: {F:0.0}",
                    $"{player.FirstName} {player.LastName}", posStr, stats.GamesPlayed, fppg);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Normal for rookies / practice squad players with no season stats
                logger.LogDebug("ESPN 404 for EspnId={Id}", player.EspnId);
                processed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ESPN fetch failed for EspnId={Id}", player.EspnId);
                failed++;
                processed++;
            }

            await Task.Delay(EspnDelay, ct);
        }

        // Bulk upsert all collected documents into simulation_results
        if (documents.Count > 0)
        {
            await simulationResultRepository.UpsertBatchAsync(documents, CancellationToken.None);
            logger.LogInformation(
                "ESPN stats sync: upserted {Count} season-average documents into simulation_results", documents.Count);
        }

        sw.Stop();
        logger.LogInformation(
            "ESPN stats sync complete — Processed: {P}, Upserted: {U}, Failed: {F}, NoEspnId: {N}, Duration: {D:0.0}s",
            processed, upserted, failed, noEspnId, sw.Elapsed.TotalSeconds);

        return new EspnStatsSyncResult(processed, upserted, failed, noEspnId, sw.Elapsed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ESPN JSON parsing
    // ─────────────────────────────────────────────────────────────────────────

    private static EspnPlayerStats? ParseEspnStats(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("splits", out var splits) ||
                !splits.TryGetProperty("categories", out var categories))
                return null;

            var result = new EspnPlayerStats();

            foreach (var category in categories.EnumerateArray())
            {
                var name = category.GetProperty("name").GetString() ?? "";
                var stats = category.GetProperty("stats");

                switch (name.ToLower())
                {
                    case "general":
                        result.GamesPlayed = GetStatValue(stats, "gamesPlayed");
                        break;
                    case "passing":
                        result.Completions = GetStatValue(stats, "completions");
                        result.Attempts = GetStatValue(stats, "passingAttempts");
                        result.PassingYards = GetStatValue(stats, "passingYards");
                        result.PassingTouchdowns = GetStatValue(stats, "passingTouchdowns");
                        result.Interceptions = GetStatValue(stats, "interceptions");
                        break;
                    case "rushing":
                        result.RushingAttempts = GetStatValue(stats, "rushingAttempts");
                        result.RushingYards = GetStatValue(stats, "rushingYards");
                        result.RushingTouchdowns = GetStatValue(stats, "rushingTouchdowns");
                        break;
                    case "receiving":
                        result.Receptions = GetStatValue(stats, "receptions");
                        result.ReceivingYards = GetStatValue(stats, "receivingYards");
                        result.ReceivingTouchdowns = GetStatValue(stats, "receivingTouchdowns");
                        break;
                }
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static double GetStatValue(JsonElement statsArray, string statName)
    {
        foreach (var stat in statsArray.EnumerateArray())
        {
            if (stat.TryGetProperty("name", out var name) &&
                name.GetString() == statName &&
                stat.TryGetProperty("value", out var val))
            {
                return val.GetDouble();
            }
        }
        return 0;
    }

    private static double ComputeFppg(EspnPlayerStats stats)
    {
        if (stats.GamesPlayed <= 0) return 0;

        var totalPts =
            (stats.PassingYards * PassYdPts) +
            (stats.PassingTouchdowns * PassTdPts) +
            (stats.Interceptions * IntPts) +
            (stats.RushingYards * RushYdPts) +
            (stats.RushingTouchdowns * RushTdPts) +
            (stats.Receptions * RecPts) +
            (stats.ReceivingYards * RecYdPts) +
            (stats.ReceivingTouchdowns * RecTdPts);

        return totalPts / stats.GamesPlayed;
    }

    private class EspnPlayerStats
    {
        public double GamesPlayed { get; set; }
        public double Completions { get; set; }
        public double Attempts { get; set; }
        public double PassingYards { get; set; }
        public double PassingTouchdowns { get; set; }
        public double Interceptions { get; set; }
        public double RushingAttempts { get; set; }
        public double RushingYards { get; set; }
        public double RushingTouchdowns { get; set; }
        public double Receptions { get; set; }
        public double ReceivingYards { get; set; }
        public double ReceivingTouchdowns { get; set; }
    }
}