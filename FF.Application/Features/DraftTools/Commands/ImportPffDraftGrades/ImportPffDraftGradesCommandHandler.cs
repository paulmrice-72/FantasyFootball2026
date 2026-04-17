// FF.Application/Features/DraftTools/Commands/ImportPffDraftGrades/ImportPffDraftGradesCommandHandler.cs
using FF.Application.Features.DraftTools.Commands.ImportConsensusAdp;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Commands.ImportPffDraftGrades;

/// <summary>
/// Imports PFF draft grades from CSV.
/// Expected header: Rank,PlayerName,Position,Team,Grade
/// Grade is PFF's 0-100 scale — stored raw, normalized in the calculator.
/// </summary>
public class ImportPffDraftGradesCommandHandler(
    IPffDraftGradeRepository pffRepository,
    IPlayerRepository playerRepository,
    ILogger<ImportPffDraftGradesCommandHandler> logger)
    : IRequestHandler<ImportPffDraftGradesCommand, Result<ImportPffDraftGradesResult>>
{
    public async Task<Result<ImportPffDraftGradesResult>> Handle(
        ImportPffDraftGradesCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = ParseCsv(request.CsvContent);
            if (rows.Count == 0)
                return Result.Failure<ImportPffDraftGradesResult>(
                    new Error("PFF_IMPORT_EMPTY", "CSV contained no parseable rows"));

            var rookies = await playerRepository.GetRookiesAsync(null, cancellationToken);
            var nameMap = rookies
                .Where(p => p.SleeperPlayerId != null)
                .ToDictionary(
                    p => NormalizeName(p.FullName),
                    p => p.SleeperPlayerId!);

            var documents = new List<PffDraftGradeDocument>();
            int unmatched = 0;

            foreach (var row in rows)
            {
                var normalized = NormalizeName(row.PlayerName);
                var sleeperPlayerId = nameMap.TryGetValue(normalized, out var id)
                    ? id : string.Empty;

                if (string.IsNullOrEmpty(sleeperPlayerId))
                {
                    logger.LogWarning("PFF Import: No Sleeper match for '{PlayerName}'", row.PlayerName);
                    unmatched++;
                }

                documents.Add(new PffDraftGradeDocument
                {
                    Id = string.IsNullOrEmpty(sleeperPlayerId)
                        ? $"unmatched-{row.Rank}"
                        : sleeperPlayerId,
                    SleeperPlayerId = sleeperPlayerId,
                    PlayerName = row.PlayerName,
                    Position = row.Position,
                    NflTeam = row.Team,
                    PffGrade = row.Grade,
                    PffRank = row.Rank,
                    Season = request.Season,
                    ImportedAt = DateTime.UtcNow
                });
            }

            await pffRepository.UpsertManyAsync(documents, cancellationToken);

            logger.LogInformation(
                "PFF Import complete — Imported: {Count}, Unmatched: {Unmatched}",
                documents.Count, unmatched);

            return Result<ImportPffDraftGradesResult>.Success(
                new ImportPffDraftGradesResult(documents.Count, unmatched, request.Season));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PFF Import failed");
            return Result.Failure<ImportPffDraftGradesResult>(
                new Error("PFF_IMPORT_ERROR", ex.Message));
        }
    }

    // Expected header: Rank,PlayerName,Position,Team,Grade
    private static List<PffRow> ParseCsv(string csv)
    {
        var rows = new List<PffRow>();
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
        int iGrade = IdxOf("grade");

        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');
            if (!int.TryParse(Safe(cols, iRank), out var rank)) continue;
            if (!double.TryParse(Safe(cols, iGrade), out var grade)) continue;

            rows.Add(new PffRow(
                Rank: rank,
                PlayerName: Safe(cols, iName),
                Position: Safe(cols, iPos).ToUpperInvariant(),
                Team: Safe(cols, iTeam).ToUpperInvariant(),
                Grade: Math.Clamp(grade, 0, 100)));
        }

        return rows;
    }

    private static string Safe(string[] cols, int idx) =>
        idx >= 0 && idx < cols.Length ? cols[idx].Trim().Trim('"') : string.Empty;

    private static string NormalizeName(string name) =>
        new string([.. name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ')])
            .Trim();

    private record PffRow(int Rank, string PlayerName, string Position, string Team, double Grade);
}