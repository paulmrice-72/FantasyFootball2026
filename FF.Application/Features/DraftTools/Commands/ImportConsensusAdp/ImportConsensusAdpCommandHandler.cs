// FF.Application/Features/DraftTools/Commands/ImportConsensusAdp/ImportConsensusAdpCommandHandler.cs
using FF.Application.Features.DraftTools.Commands.ImportPffDraftGrades;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Commands.ImportConsensusAdp;

/// <summary>
/// Imports consensus rookie ADP from CSV.
/// Expected header: Rank,PlayerName,Position,Team,ADP
/// ADP is a pick number (1.0 = best) — stored raw, normalized in the calculator.
/// Source label (e.g. "NFFC", "Underdog", "Sleeper") stored for transparency.
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
                .ToDictionary(
                    p => NormalizeName(p.FullName),
                    p => p.SleeperPlayerId!);

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

    // Expected header: Rank,PlayerName,Position,Team,ADP
    private static List<AdpRow> ParseCsv(string csv)
    {
        var rows = new List<AdpRow>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return rows;

        var headers = lines[0].Split(',')
            .Select(h => h.Trim().ToLowerInvariant())
            .ToArray();

        int IdxOf(string name) => Array.IndexOf(headers, name);

        int iRank = IdxOf("rank");
        int iName = IdxOf("playername");
        int iPos = IdxOf("position");
        int iTeam = IdxOf("team");
        int iAdp = IdxOf("adp");

        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');
            if (!int.TryParse(Safe(cols, iRank), out var rank)) continue;
            if (!double.TryParse(Safe(cols, iAdp), out var adp)) continue;

            rows.Add(new AdpRow(
                Rank: rank,
                PlayerName: Safe(cols, iName),
                Position: Safe(cols, iPos).ToUpperInvariant(),
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