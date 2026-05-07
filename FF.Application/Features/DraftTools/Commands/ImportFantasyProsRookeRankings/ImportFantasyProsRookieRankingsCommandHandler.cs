// FF.Application/DraftTools/Commands/ImportFantasyProsRookieRankings/ImportFantasyProsRookieRankingsCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using FF.Application.Interfaces.Repositories;

namespace FF.Application.Features.DraftTools.Commands.ImportFantasyProsRookeRankings;

public class ImportFantasyProsRookieRankingsCommandHandler(
    IFantasyProsRookieRankingRepository rankingRepository,
    IPlayerRepository playerRepository,
    ILogger<ImportFantasyProsRookieRankingsCommandHandler> logger)
    : IRequestHandler<ImportFantasyProsRookieRankingsCommand, Result<ImportFantasyProsResult>>
{
    public async Task<Result<ImportFantasyProsResult>> Handle(
        ImportFantasyProsRookieRankingsCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = ParseCsv(request.CsvContent);
            if (rows.Count == 0)
                return Result.Failure<ImportFantasyProsResult>(
                    new Error("FP_IMPORT_EMPTY", "CSV contained no parseable rows"));

            var rookies = await playerRepository.GetRookiesAsync(null, cancellationToken);

            var nameMap = rookies
                .Where(p => p.SleeperPlayerId != null)
                .GroupBy(p => NormalizeName(p.FullName))
                .ToDictionary(g => g.Key, g => g.First().SleeperPlayerId!);

            var documents = new List<FantasyProsRookieRankingDocument>();
            int unmatched = 0;

            foreach (var row in rows)
            {
                var normalizedName = NormalizeName(row.PlayerName);
                var sleeperPlayerId = nameMap.TryGetValue(normalizedName, out var id)
                    ? id : string.Empty;

                if (string.IsNullOrEmpty(sleeperPlayerId))
                {
                    logger.LogWarning("FP Import: No Sleeper match for '{PlayerName}'", row.PlayerName);
                    unmatched++;
                }

                // Id includes season so imports from different years don't collide.
                // Format: "{sleeperPlayerId}-{season}" for matched, "unmatched-{rank}-{season}" for misses.
                var docId = string.IsNullOrEmpty(sleeperPlayerId)
                    ? $"unmatched-{row.Rank}-{request.Season}"
                    : $"{sleeperPlayerId}-{request.Season}";

                documents.Add(new FantasyProsRookieRankingDocument
                {
                    Id = docId,
                    SleeperPlayerId = sleeperPlayerId,
                    PlayerName = row.PlayerName,
                    Position = row.Position,
                    NflTeam = row.Team,
                    FantasyProsRank = row.Rank,
                    PositionRank = row.PositionRank,
                    Tier = row.Tier,
                    Season = request.Season,
                    ImportedAt = DateTime.UtcNow
                });
            }

            // Deduplicate on Id — same player listed twice in CSV, keep better rank.
            documents = documents
                .GroupBy(d => d.Id)
                .Select(g => g.OrderBy(d => d.FantasyProsRank).First())
                .ToList();

            await rankingRepository.UpsertManyAsync(documents, cancellationToken);

            logger.LogInformation(
                "FP Import complete — Imported: {Count}, Unmatched: {Unmatched}",
                documents.Count, unmatched);

            return Result<ImportFantasyProsResult>.Success(
                new ImportFantasyProsResult(documents.Count, unmatched, request.Season));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FP Import failed");
            return Result.Failure<ImportFantasyProsResult>(
                new Error("FP_IMPORT_ERROR", ex.Message));
        }
    }

    // ── CSV parsing ──────────────────────────────────────────────────────────
    private static List<FpRow> ParseCsv(string csv)
    {
        var rows = new List<FpRow>();
        var normalized = csv.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return rows;

        var headers = lines[0].Split(',')
            .Select(h => h.Trim().Trim('"').ToLowerInvariant())
            .ToArray();

        int IdxOf(params string[] names)
        {
            foreach (var name in names)
            {
                var idx = Array.IndexOf(headers, name);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        int iRank = IdxOf("rk", "rank");
        int iName = IdxOf("player name", "playername");
        int iPos = IdxOf("pos", "position");
        int iTeam = IdxOf("team");

        foreach (var line in lines.Skip(1))
        {
            var cols = SplitCsvLine(line);
            if (cols.Length <= Math.Max(iRank, iName)) continue;

            var rankStr = Safe(cols, iRank).Trim();
            if (!int.TryParse(rankStr, out var rank)) continue;

            var rawPos = Safe(cols, iPos).ToUpperInvariant();
            var position = new string(rawPos.TakeWhile(char.IsLetter).ToArray());
            var posRankStr = new string(rawPos.SkipWhile(char.IsLetter).ToArray());
            int.TryParse(posRankStr, out var positionRank);

            rows.Add(new FpRow(
                Rank: rank,
                PlayerName: Safe(cols, iName),
                Position: string.IsNullOrEmpty(position) ? "UNK" : position,
                Team: Safe(cols, iTeam).ToUpperInvariant().Trim('"'),
                PositionRank: positionRank,
                Tier: null));
        }

        return rows;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim().Trim('"'));
                current.Clear();
            }
            else { current.Append(ch); }
        }
        result.Add(current.ToString().Trim().Trim('"'));
        return result.ToArray();
    }

    private static string Safe(string[] cols, int idx) =>
        idx >= 0 && idx < cols.Length ? cols[idx].Trim().Trim('"') : string.Empty;

    private static string NormalizeName(string name) =>
        new string([.. name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ')])
            .Trim();

    private record FpRow(int Rank, string PlayerName, string Position,
        string Team, int PositionRank, string? Tier);
}