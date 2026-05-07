// FF.Application/Features/DraftTools/Commands/ImportConsensusAdp/ImportConsensusAdpCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Commands.ImportConsensusAdp;

/// <summary>
/// Imports consensus rookie ADP from CSV.
///
/// Supported column layouts (case-insensitive):
///   FantasyPros ADP export:  "Rank","Player","Team","Bye","POS","AVG"
///   Manual/custom format:    Rank,PlayerName,Position,Team,ADP
///
/// Position suffix (e.g. RB1, WR2) is stripped automatically.
/// ADP is a pick number (1.0 = best) — stored raw, normalized in the calculator.
/// </summary>
public class ImportConsensusAdpCommandHandler(
    IConsensusAdpRepository adpRepository,
    IPlayerRepository playerRepository,
    ILogger<ImportConsensusAdpCommandHandler> logger)
    : IRequestHandler<ImportConsensusAdpCommand, Result<ImportConsensusAdpResult>>
{
    public async Task<Result<ImportConsensusAdpResult>> Handle(
        ImportConsensusAdpCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = ParseCsv(request.CsvContent);
            if (rows.Count == 0)
                return Result.Failure<ImportConsensusAdpResult>(
                    new Error("ADP_IMPORT_EMPTY", "CSV contained no parseable rows"));

            var rookies = await playerRepository.GetRookiesAsync(null, cancellationToken);
            var nameMap = rookies
                .Where(p => p.SleeperPlayerId != null)
                .GroupBy(p => NormalizeName(p.FullName))
                .ToDictionary(g => g.Key, g => g.First().SleeperPlayerId!);

            var documents = new List<ConsensusAdpDocument>();
            int unmatched = 0;

            foreach (var row in rows)
            {
                var normalized = NormalizeName(row.PlayerName);
                var sleeperPlayerId = nameMap.TryGetValue(normalized, out var id)
                    ? id : string.Empty;

                if (string.IsNullOrEmpty(sleeperPlayerId))
                {
                    logger.LogWarning("ADP Import: No Sleeper match for '{PlayerName}'", row.PlayerName);
                    unmatched++;
                }

                documents.Add(new ConsensusAdpDocument
                {
                    Id = string.IsNullOrEmpty(sleeperPlayerId)
                        ? $"unmatched-{row.Rank}"
                        : sleeperPlayerId,
                    SleeperPlayerId = sleeperPlayerId,
                    PlayerName = row.PlayerName,
                    Position = row.Position,
                    NflTeam = row.Team,
                    Adp = row.Adp,
                    AdpRank = row.Rank,
                    Source = request.Source,
                    Season = request.Season,
                    ImportedAt = DateTime.UtcNow
                });
            }

            // Dedup on Id — same player can appear twice in FP exports (e.g. listed at
            // multiple positions). Keep the lower (better) ADP rank entry.
            documents = documents
                .GroupBy(d => d.Id)
                .Select(g => g.OrderBy(d => d.AdpRank ?? int.MaxValue).First())
                .ToList();

            await adpRepository.UpsertManyAsync(documents, cancellationToken);

            logger.LogInformation(
                "ADP Import complete — Source: {Source}, Imported: {Count}, Unmatched: {Unmatched}",
                request.Source, documents.Count, unmatched);

            return Result<ImportConsensusAdpResult>.Success(
                new ImportConsensusAdpResult(documents.Count, unmatched, request.Season, request.Source));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ADP Import failed");
            return Result.Failure<ImportConsensusAdpResult>(
                new Error("ADP_IMPORT_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Parses both supported CSV layouts:
    ///   FP export:  "Rank","Player","Team","Bye","POS","AVG"
    ///   Custom:      Rank,PlayerName,Position,Team,ADP
    /// Column lookup is case-insensitive and tries multiple aliases per field.
    /// </summary>
    private static List<AdpRow> ParseCsv(string csv)
    {
        var rows = new List<AdpRow>();
        var lines = csv.Replace("\r\n", "\n").Replace("\r", "\n")
                       .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2) return rows;

        var headers = lines[0].Split(',')
            .Select(h => h.Trim().Trim('"').ToLowerInvariant())
            .ToArray();

        // Accept multiple column name aliases per field
        int IdxOf(params string[] names)
        {
            foreach (var name in names)
            {
                var idx = Array.IndexOf(headers, name);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        int iRank = IdxOf("rank");
        int iName = IdxOf("player", "playername", "player name");   // FP uses "player"
        int iPos = IdxOf("pos", "position");
        int iTeam = IdxOf("team");
        int iAdp = IdxOf("avg", "adp");                            // FP uses "avg"

        if (iRank < 0 || iName < 0 || iAdp < 0)
        {
            // Return empty — caller will surface the "no parseable rows" error
            return rows;
        }

        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');

            var rankStr = Safe(cols, iRank);
            if (!int.TryParse(rankStr, out var rank)) continue;

            var adpStr = Safe(cols, iAdp);
            if (!double.TryParse(adpStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var adp)) continue;

            // Strip position rank suffix: "RB1" → "RB", "WR12" → "WR"
            var rawPos = Safe(cols, iPos).ToUpperInvariant();
            var position = new string(rawPos.TakeWhile(char.IsLetter).ToArray());
            if (string.IsNullOrEmpty(position)) position = "UNK";

            rows.Add(new AdpRow(
                Rank: rank,
                PlayerName: Safe(cols, iName),
                Position: position,
                Team: Safe(cols, iTeam).ToUpperInvariant(),
                Adp: Math.Max(1, adp)));
        }

        return rows;
    }

    private static string Safe(string[] cols, int idx) =>
        idx >= 0 && idx < cols.Length ? cols[idx].Trim().Trim('"') : string.Empty;

    private static string NormalizeName(string name) =>
        new string([.. name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ')])
            .Trim();

    private record AdpRow(int Rank, string PlayerName, string Position, string Team, double Adp);
}