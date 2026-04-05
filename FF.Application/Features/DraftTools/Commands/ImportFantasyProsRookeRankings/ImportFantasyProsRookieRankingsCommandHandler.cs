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
                return (Result<ImportFantasyProsResult>)Result<ImportFantasyProsResult>.Failure(
                    new Error("FP_IMPORT_EMPTY", "CSV contained no parseable rows"));

            // Load all rookies for name matching
            var rookies = await playerRepository.GetRookiesAsync(null, cancellationToken);
            var nameMap = rookies
                .Where(p => p.SleeperPlayerId != null)
                .ToDictionary(
                    p => NormalizeName(p.FullName),
                    p => p.SleeperPlayerId!);

            var documents = new List<FantasyProsRookieRankingDocument>();
            int unmatched = 0;

            foreach (var row in rows)
            {
                var normalizedName = NormalizeName(row.PlayerName);
                var sleeperPlayerId = nameMap.TryGetValue(normalizedName, out var id)
                    ? id
                    : string.Empty;

                if (string.IsNullOrEmpty(sleeperPlayerId))
                {
                    logger.LogWarning(
                        "FP Import: No Sleeper match for '{PlayerName}'", row.PlayerName);
                    unmatched++;
                }

                documents.Add(new FantasyProsRookieRankingDocument
                {
                    Id = string.IsNullOrEmpty(sleeperPlayerId)
                        ? $"unmatched-{row.Rank}"
                        : sleeperPlayerId,
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
            return (Result<ImportFantasyProsResult>)Result<ImportFantasyProsResult>.Failure(
                new Error("FP_IMPORT_ERROR", ex.Message));
        }
    }

    // ── CSV parsing ───────────────────────────────────────────────────────
    // Expected header: Rank,PlayerName,Position,Team,PositionRank,Tier
    // PositionRank and Tier columns are optional
    private static List<FpRow> ParseCsv(string csv)
    {
        var rows = new List<FpRow>();
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
        int iPosRank = IdxOf("positionrank");
        int iTier = IdxOf("tier");

        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');
            if (cols.Length <= Math.Max(iRank, iName)) continue;

            if (!int.TryParse(Safe(cols, iRank), out var rank)) continue;

            rows.Add(new FpRow(
                Rank: rank,
                PlayerName: Safe(cols, iName),
                Position: Safe(cols, iPos).ToUpperInvariant(),
                Team: Safe(cols, iTeam).ToUpperInvariant(),
                PositionRank: int.TryParse(Safe(cols, iPosRank), out var pr) ? pr : 0,
                Tier: iTier >= 0 ? Safe(cols, iTier) : null
            ));
        }

        return rows;
    }

    private static string Safe(string[] cols, int idx) =>
        idx >= 0 && idx < cols.Length ? cols[idx].Trim().Trim('"') : string.Empty;

    // Normalize: lowercase, strip punctuation, handle "Jr", "III" etc
    private static string NormalizeName(string name) =>
        new string([.. name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ')])
        .Trim();

    private record FpRow(int Rank, string PlayerName, string Position,
        string Team, int PositionRank, string? Tier);
}