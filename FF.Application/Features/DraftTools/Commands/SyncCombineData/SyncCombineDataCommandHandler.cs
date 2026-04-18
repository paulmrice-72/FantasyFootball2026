// FF.Application/Features/DraftTools/Commands/SyncCombineData/SyncCombineDataCommandHandler.cs
using System.Globalization;
using System.Net.Http;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;


namespace FF.Application.Features.DraftTools.Commands.SyncCombineData;

/// <summary>
/// Downloads nflverse combine.csv, name-matches players to SleeperPlayerIds,
/// computes athleticism scores, upserts to MongoDB combine_results collection.
/// Also backfills BirthDate on the Player entity in PostgreSQL.
///
/// URL: https://github.com/nflverse/nflverse-data/releases/download/combine/combine.csv
/// CSV columns (relevant):
///   season, player_name, pos, school, ht, wt, forty, bench, vertical,
///   broad_jump, cone, shuttle, birth_date, draft_year, pfr_id, cfb_id
/// </summary>
public class SyncCombineDataCommandHandler(
    IHttpClientFactory httpClientFactory,
    IPlayerRepository playerRepository,
    ICombineResultRepository combineRepository,
    ILogger<SyncCombineDataCommandHandler> logger)
    : IRequestHandler<SyncCombineDataCommand, Result<SyncCombineDataResult>>
{
    private const string CombineUrl =
        "https://github.com/nflverse/nflverse-data/releases/download/combine/combine.csv";

    public async Task<Result<SyncCombineDataResult>> Handle(
        SyncCombineDataCommand request,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        logger.LogInformation("Starting combine sync for season {Season}", request.Season);

        // ── 1. Download CSV ───────────────────────────────────────────────
        string csv;
        try
        {
            var http = httpClientFactory.CreateClient("NflverseClient");
            csv = await http.GetStringAsync(CombineUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download combine CSV");
            return Result.Failure<SyncCombineDataResult>(
                new Error("CombineSync.DownloadFailed", $"Failed to download combine data: {ex.Message}"));
        }

        // ── 2. Parse CSV ──────────────────────────────────────────────────
        var rows = ParseCsv(csv, request.Season);
        logger.LogInformation("Parsed {Count} combine rows for season {Season}", rows.Count, request.Season);

        if (!rows.Any())
            return Result.Failure<SyncCombineDataResult>(
                new Error("CombineSync.NoData", $"No combine rows found for season {request.Season}"));

        // ── 3. Load all players from DB for name matching ─────────────────
        var allPlayers = await playerRepository.GetAllAsync(cancellationToken);
        // Build lookup: normalized full name → player (position-bucketed for disambiguation)
        var playerLookup = allPlayers
            .Where(p => p.SleeperPlayerId != null)
            .GroupBy(p => NormalizeName(p.FullName))
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── 4. Match, score, build documents ─────────────────────────────
        var documents = new List<CombineResultDocument>();
        var matched = 0;
        var unmatched = 0;

        foreach (var row in rows)
        {
            var sleeperPlayerId = ResolveSleeperPlayerId(row, playerLookup);

            if (sleeperPlayerId is null)
            {
                logger.LogDebug("No match for combine player: {Name} ({Pos})", row.PlayerName, row.Pos);
                unmatched++;
                continue;
            }

            var speedScore = row.Pos == "RB"
                ? AthleticismScoreCalculator.ComputeSpeedScore(row.WeightLbs, row.FortyYard)
                : null;

            var doc = new CombineResultDocument
            {
                Id = $"{sleeperPlayerId}_{row.Season}",
                SleeperPlayerId = sleeperPlayerId,
                PlayerName = row.PlayerName,
                Position = row.Pos,
                School = row.School,
                Season = row.Season,
                HeightInches = row.HeightInches,
                WeightLbs = row.WeightLbs,
                FortyYard = row.FortyYard,
                BenchReps = row.BenchReps,
                Vertical = row.Vertical,
                BroadJump = row.BroadJump,
                ConeDrill = row.ConeDrill,
                Shuttle = row.Shuttle,
                SpeedScore = speedScore,
                BirthDate = row.BirthDate,
                SyncedAt = DateTime.UtcNow
            };

            // Compute athleticism score
            doc.AthleticismScore = AthleticismScoreCalculator.Calculate(doc);

            documents.Add(doc);
            matched++;
        }

        // ── 5. Upsert to MongoDB ──────────────────────────────────────────
        await combineRepository.UpsertManyAsync(documents, cancellationToken);

        // ── 6. Backfill BirthDate on Player entities ──────────────────────
        await BackfillBirthDatesAsync(documents, allPlayers, cancellationToken);

        var duration = DateTime.UtcNow - started;
        logger.LogInformation(
            "Combine sync complete — {Matched} matched, {Unmatched} unmatched in {Duration:g}",
            matched, unmatched, duration);

        return Result<SyncCombineDataResult>.Success(
            new SyncCombineDataResult(matched, unmatched, rows.Count, duration));
    }

    // ── CSV parsing ───────────────────────────────────────────────────────
    private static List<CombineRow> ParseCsv(string csv, int season)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return [];

        var headers = SplitCsvLine(lines[0]);
        var idx = BuildIndex(headers);

        var rows = new List<CombineRow>();
        foreach (var line in lines.Skip(1))
        {
            var cols = SplitCsvLine(line);
            if (cols.Length < headers.Length) continue;

            // Filter to requested season
            var rowSeason = GetInt(cols, idx, "season") ?? GetInt(cols, idx, "draft_year");
            if (rowSeason != season) continue;

            var pos = Get(cols, idx, "pos")?.ToUpperInvariant();
            if (pos is null or "OL" or "DL" or "LB" or "DB" or "CB" or "S" or "OT" or "C" or "G" or "DE" or "DT")
                continue; // skip non-skill positions

            rows.Add(new CombineRow(
                Season: rowSeason.Value,
                PlayerName: Get(cols, idx, "player_name") ?? string.Empty,
                Pos: pos,
                School: Get(cols, idx, "school"),
                HeightInches: ParseHeight(Get(cols, idx, "ht")),
                WeightLbs: GetDouble(cols, idx, "wt"),
                FortyYard: GetDouble(cols, idx, "forty"),
                BenchReps: GetInt(cols, idx, "bench"),
                Vertical: GetDouble(cols, idx, "vertical"),
                BroadJump: GetDouble(cols, idx, "broad_jump"),
                ConeDrill: GetDouble(cols, idx, "cone"),
                Shuttle: GetDouble(cols, idx, "shuttle"),
                BirthDate: Get(cols, idx, "birth_date")));
        }

        return rows;
    }

    private static Dictionary<string, int> BuildIndex(string[] headers)
    {
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
            idx[headers[i].Trim('"', ' ')] = i;
        return idx;
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var inQuotes = false;
        var current = new System.Text.StringBuilder();
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); continue; }
            current.Append(ch);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static string? Get(string[] cols, Dictionary<string, int> idx, string key) =>
        idx.TryGetValue(key, out var i) && i < cols.Length
            ? string.IsNullOrWhiteSpace(cols[i]) || cols[i] == "NA" ? null : cols[i].Trim()
            : null;

    private static double? GetDouble(string[] cols, Dictionary<string, int> idx, string key)
    {
        var v = Get(cols, idx, key);
        return v is not null && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static int? GetInt(string[] cols, Dictionary<string, int> idx, string key)
    {
        var v = Get(cols, idx, key);
        return v is not null && int.TryParse(v, out var i) ? i : null;
    }

    /// <summary>
    /// Parses nflverse height format "6-2" → 74.0 inches.
    /// </summary>
    private static double? ParseHeight(string? ht)
    {
        if (ht is null) return null;
        var parts = ht.Split('-');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var feet)
            && int.TryParse(parts[1], out var inches))
            return (feet * 12) + inches;
        return null;
    }

    // ── Name matching ─────────────────────────────────────────────────────

    private static string? ResolveSleeperPlayerId(
        CombineRow row,
        Dictionary<string, List<FF.Domain.Entities.Player>> lookup)
    {
        var key = NormalizeName(row.PlayerName);
        if (!lookup.TryGetValue(key, out var candidates)) return null;

        // Position match preferred
        var pos = row.Pos.ToUpperInvariant();
        var posMatch = candidates.FirstOrDefault(p => p.Position.ToString() == pos);
        return posMatch?.SleeperPlayerId ?? candidates.First().SleeperPlayerId;
    }

    private static string NormalizeName(string name) =>
        name.ToLowerInvariant()
            .Replace("jr.", "").Replace("sr.", "").Replace("iii", "")
            .Replace("ii", "").Replace("iv", "").Replace("'", "")
            .Replace("-", " ").Replace(".", "")
            .Trim()
            .Replace("  ", " ");

    // ── BirthDate backfill ────────────────────────────────────────────────

    private async Task BackfillBirthDatesAsync(
        List<CombineResultDocument> docs,
        IReadOnlyList<FF.Domain.Entities.Player> allPlayers,
        CancellationToken cancellationToken)
    {
        var playerById = allPlayers
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!);

        foreach (var doc in docs.Where(d => d.BirthDate is not null))
        {
            if (!playerById.TryGetValue(doc.SleeperPlayerId, out var player)) continue;
            if (player.BirthDate is not null) continue; // already set

            if (DateOnly.TryParse(doc.BirthDate, out var birthDate))
            {
                player.UpdateBirthDate(birthDate);
                await playerRepository.UpdateAsync(player, cancellationToken);
            }
        }
    }

    private record CombineRow(
        int Season,
        string PlayerName,
        string Pos,
        string? School,
        double? HeightInches,
        double? WeightLbs,
        double? FortyYard,
        int? BenchReps,
        double? Vertical,
        double? BroadJump,
        double? ConeDrill,
        double? Shuttle,
        string? BirthDate);
}