using FF.Application.Features.DraftTools.Commands.ImportFantasyProsRookeRankings;
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using FF.Application.Services;

namespace FF.Application.Features.DraftTools.Commands.ImportFantasyProsDynastyRankings;

public class ImportFantasyProsDynastyRankingsCommandHandler(
    IFantasyProsRookieRankingRepository rankingRepository,
    IPlayerRepository playerRepository,
    ILogger<ImportFantasyProsDynastyRankingsCommandHandler> logger)
    : IRequestHandler<ImportFantasyProsDynastyRankingsCommand, Result<ImportFantasyProsResult>>
{
    public async Task<Result<ImportFantasyProsResult>> Handle(
        ImportFantasyProsDynastyRankingsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var rows = ParseCsv(request.CsvContent);
            if (rows.Count == 0)
                return Result.Failure<ImportFantasyProsResult>(
                    new Error("FP_DYNASTY_IMPORT_EMPTY", "CSV contained no parseable rows"));

            // Load ALL active players — dynasty rankings cover the full roster, not just rookies
            var allPlayers = await playerRepository.GetAllAsync(cancellationToken);

            // 2026-09-07: this was GroupBy(name).First() — no position tiebreak,
            // so with two Kenneth Walkers in the player table (8151 RB, 4634 WR)
            // the winner was whichever Mongo returned first. That is the same
            // defect FAN-131 fixed in the season-average seed on 09-02, still
            // live here. Keep every candidate and disambiguate deliberately.
            var eligible = allPlayers.Where(p => p.SleeperPlayerId != null).ToList();

            var byName = eligible
                .GroupBy(p => NormalizeName(p.FullName))
                .ToDictionary(g => g.Key, g => g.ToList());

            // Nickname fallback index. FantasyPros publishes some players under a
            // nickname rather than their roster name — "Hollywood Brown" for
            // Marquise Brown, "Bam Knight" for Zonovan Knight — and no amount of
            // string normalization turns one into the other. Surname plus position
            // plus team identifies them when it is UNIQUE, and refuses when it is
            // not, which is the same principle the season-average seed uses.
            var bySurname = eligible
                .GroupBy(p => SurnameKey(NormalizeName(p.FullName), p.Position.ToString(), p.NflTeam))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.ToList());

            var documents = new List<FantasyProsRookieRankingDocument>();
            int unmatched = 0;
            int aliasMatched = 0;

            foreach (var row in rows)
            {
                var normalizedName = NormalizeName(row.PlayerName);
                var sleeperPlayerId = ResolveSleeperId(
                    row, normalizedName, byName, bySurname, out var viaAlias);

                if (viaAlias) aliasMatched++;

                if (string.IsNullOrEmpty(sleeperPlayerId))
                {
                    logger.LogWarning(
                        "FP Dynasty Import: No Sleeper match for '{PlayerName}' ({Position} {Team})",
                        row.PlayerName, row.Position, row.Team);
                    unmatched++;
                }

                documents.Add(new FantasyProsRookieRankingDocument
                {
                    Id = string.IsNullOrEmpty(sleeperPlayerId)
                        ? $"dynasty-unmatched-{row.Rank}"
                        : $"dynasty-{sleeperPlayerId}",
                    SleeperPlayerId = sleeperPlayerId,
                    PlayerName = row.PlayerName,
                    Position = row.Position,
                    NflTeam = row.Team,
                    FantasyProsRank = row.Rank,
                    PositionRank = row.PositionRank,
                    Tier = row.Tier,
                    Season = request.Season,
                    RankingType = "Dynasty",
                    ImportedAt = DateTime.UtcNow
                });
            }

            await rankingRepository.UpsertManyAsync(documents, cancellationToken);

            logger.LogInformation(
                "FP Dynasty Import complete — Imported: {Count}, Unmatched: {Unmatched}, " +
                "MatchedByAlias: {AliasMatched}",
                documents.Count, unmatched, aliasMatched);

            return Result<ImportFantasyProsResult>.Success(
                new ImportFantasyProsResult(documents.Count, unmatched, request.Season));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FP Dynasty Import failed");
            return Result.Failure<ImportFantasyProsResult>(
                new Error("FP_DYNASTY_IMPORT_ERROR", ex.Message));
        }
    }

    // Same CSV format as the rookie import — reused parser
    // "RK",TIERS,"PLAYER NAME",TEAM,"POS","AGE","BEST","WORST","AVG.","STD.DEV","ECR VS. ADP"
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

    /// <summary>
    /// Resolves one FantasyPros row to a Sleeper id, most-trusted signal first.
    ///
    ///   1. Exact normalized name. On a collision, narrow by position, then by
    ///      team. Still ambiguous after both — refuse rather than guess, because
    ///      a wrong bind writes one player's ranking onto another player's id and
    ///      nothing downstream can tell that apart from real data.
    ///   2. Surname + position + team, only when it identifies exactly one
    ///      player. This is what catches nicknames.
    ///
    /// Anything else is left unmatched and logged.
    /// </summary>
    private string ResolveSleeperId(
        FpRow row,
        string normalizedName,
        Dictionary<string, List<FF.Domain.Entities.Player>> byName,
        Dictionary<string, List<FF.Domain.Entities.Player>> bySurname,
        out bool viaAlias)
    {
        viaAlias = false;

        if (byName.TryGetValue(normalizedName, out var candidates))
        {
            if (candidates.Count == 1)
                return candidates[0].SleeperPlayerId!;

            var samePosition = candidates
                .Where(c => string.Equals(c.Position.ToString(), row.Position,
                                          StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (samePosition.Count == 1)
                return samePosition[0].SleeperPlayerId!;

            var sameTeam = samePosition
                .Where(c => string.Equals(c.NflTeam, row.Team, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sameTeam.Count == 1)
                return sameTeam[0].SleeperPlayerId!;

            logger.LogWarning(
                "FP Dynasty Import: '{Name}' ({Pos} {Team}) matched {Count} players " +
                "[{Ids}] — refusing to guess.",
                row.PlayerName, row.Position, row.Team, candidates.Count,
                string.Join(", ", candidates.Select(c => $"{c.SleeperPlayerId}:{c.Position}:{c.NflTeam ?? "-"}")));

            return string.Empty;
        }

        var surnameKey = SurnameKey(normalizedName, row.Position, row.Team);

        if (!string.IsNullOrEmpty(surnameKey)
            && bySurname.TryGetValue(surnameKey, out var bySurnameCandidates)
            && bySurnameCandidates.Count == 1)
        {
            var match = bySurnameCandidates[0];

            logger.LogInformation(
                "FP Dynasty Import: matched '{FpName}' to '{RosterName}' ({Pos} {Team}) " +
                "by surname — likely a nickname on FantasyPros' side.",
                row.PlayerName, match.FullName, row.Position, row.Team);

            viaAlias = true;
            return match.SleeperPlayerId!;
        }

        return string.Empty;
    }

    /// <summary>
    /// Last token of the normalized name, plus position and team. Both sides of
    /// the comparison are built the same way so a multi-word surname such as
    /// "St. Brown" reduces identically on each.
    ///
    /// Returns empty when team is unknown — a surname match with no team is far
    /// too loose to trust, and an empty key is skipped by the caller.
    /// </summary>
    private static string SurnameKey(string normalizedName, string position, string? team)
    {
        if (string.IsNullOrWhiteSpace(team)) return string.Empty;

        var surname = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        return string.IsNullOrEmpty(surname)
            ? string.Empty
            : $"{surname}|{position.ToUpperInvariant()}|{team.ToUpperInvariant()}";
    }

    // 2026-09-07: this used to lowercase and strip punctuation but leave
    // generational suffixes intact, so FantasyPros' "Patrick Mahomes II" never
    // matched Sleeper's "Patrick Mahomes". Nine of the ten most valuable players
    // the calibration harness failed to match were suffix mismatches, and 84 of
    // our top 250 valuations were silently excluded from every metric.
    //
    // Now delegates to the shared normalizer so this file cannot drift from
    // SyncRedraftAdpJob again — which is exactly how the two rules diverged.
    private static string NormalizeName(string name) =>
        PlayerNameNormalizer.Normalize(name);

    private record FpRow(int Rank, string PlayerName, string Position,
        string Team, int PositionRank, string? Tier);
}