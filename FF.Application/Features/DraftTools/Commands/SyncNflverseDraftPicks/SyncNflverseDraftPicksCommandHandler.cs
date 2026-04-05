// FF.Application/Features/DraftTools/Commands/SyncNflverseDraftPicks/SyncNflverseDraftPicksCommandHandler.cs
using FF.Application.Common.Models;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Commands.SyncNflverseDraftPicks;

/// <summary>
/// Downloads nflverse draft picks CSV for the given season and populates
/// DraftRound / DraftPick / CollegeTeam on matching Players rows.
/// CSV URL: https://github.com/nflverse/nfldata/raw/master/data/draft_picks.csv
/// Matching: SleeperPlayerId via GsisId first, then name fuzzy match fallback.
/// </summary>
public class SyncNflverseDraftPicksCommandHandler(
    IPlayerRepository playerRepository,
    IHttpClientFactory httpClientFactory,
    ILogger<SyncNflverseDraftPicksCommandHandler> logger)
    : IRequestHandler<SyncNflverseDraftPicksCommand, Result<SyncDraftPicksResult>>
{
    private const string NflverseDraftPicksUrl =
        "https://github.com/nflverse/nfldata/raw/master/data/draft_picks.csv";

    public async Task<Result<SyncDraftPicksResult>> Handle(
        SyncNflverseDraftPicksCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1 — Download CSV
            var client = httpClientFactory.CreateClient("NflverseClient");
            var csv = await client.GetStringAsync(NflverseDraftPicksUrl, cancellationToken);

            if (string.IsNullOrWhiteSpace(csv))
                return (Result<SyncDraftPicksResult>)Result<SyncDraftPicksResult>.Failure(
                    new Error("NFLVERSE_EMPTY", "Draft picks CSV was empty."));

            // 2 — Parse rows for this season
            var picks = ParseCsv(csv, request.Season);
            if (picks.Count == 0)
            {
                logger.LogWarning(
                    "NflverseDraftPickSync: No picks found for season {Season}. " +
                    "Draft may not have occurred yet.", request.Season);
                return Result<SyncDraftPicksResult>.Success(
                    new SyncDraftPicksResult(0, 0, 0));
            }

            logger.LogInformation(
                "NflverseDraftPickSync: {Count} picks found for {Season}",
                picks.Count, request.Season);

            // 3 — Load all rookies for matching
            var rookies = await playerRepository.GetRookiesAsync(null, cancellationToken);
            var gsisMap = rookies
                .Where(p => !string.IsNullOrEmpty(p.GsisId))
                .ToDictionary(p => p.GsisId!, p => p);

            var nameMap = rookies
                .Where(p => p.SleeperPlayerId != null)
                .ToDictionary(p => NormalizeName(p.FullName), p => p);

            int matched = 0, unmatched = 0;

            // 4 — Match and update
            foreach (var pick in picks)
            {
                // Try GsisId first (most reliable)
                var player = !string.IsNullOrEmpty(pick.GsisId) &&
                             gsisMap.TryGetValue(pick.GsisId, out var byGsis)
                    ? byGsis
                    : nameMap.TryGetValue(NormalizeName(pick.PlayerName), out var byName)
                        ? byName
                        : null;

                if (player is null)
                {
                    logger.LogWarning(
                        "NflverseDraftPickSync: No match for '{Player}' (R{Round} P{Pick})",
                        pick.PlayerName, pick.Round, pick.Pick);
                    unmatched++;
                    continue;
                }

                player.UpdateDraftCapital(pick.Round, pick.Pick, pick.College);
                await playerRepository.UpdateAsync(player, cancellationToken);
                matched++;

                logger.LogDebug(
                    "NflverseDraftPickSync: Matched {Player} → R{Round} P{Pick}",
                    pick.PlayerName, pick.Round, pick.Pick);
            }

            logger.LogInformation(
                "NflverseDraftPickSync complete — Matched: {Matched}, Unmatched: {Unmatched}",
                matched, unmatched);

            return Result<SyncDraftPicksResult>.Success(
                new SyncDraftPicksResult(matched, unmatched, picks.Count));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NflverseDraftPickSync failed");
            return (Result<SyncDraftPicksResult>)Result<SyncDraftPicksResult>.Failure(
                new Error("NFLVERSE_ERROR", ex.Message));
        }
    }

    // ── CSV parsing ───────────────────────────────────────────────────────
    // nflverse draft_picks.csv columns (relevant):
    // season, round, pick, team, pfr_player_name, position, college, pfr_id, gsis_id
    private static List<DraftPickRow> ParseCsv(string csv, int season)
    {
        var rows = new List<DraftPickRow>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return rows;

        var headers = lines[0].Split(',')
            .Select(h => h.Trim().Trim('"').ToLowerInvariant())
            .ToArray();

        int Idx(string name) => Array.IndexOf(headers, name);

        int iSeason = Idx("season");
        int iRound = Idx("round");
        int iPick = Idx("pick");
        int iName = Idx("pfr_player_name");
        int iPos = Idx("position");
        int iCollege = Idx("college");
        int iGsis = Idx("gsis_id");

        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');

            var seasonVal = Safe(cols, iSeason).Trim('"');
            if (!int.TryParse(seasonVal, out var s) || s != season) continue;
            if (!int.TryParse(Safe(cols, iRound), out var round)) continue;
            if (!int.TryParse(Safe(cols, iPick), out var pick)) continue;

            rows.Add(new DraftPickRow(
                Round: round,
                Pick: pick,
                PlayerName: Safe(cols, iName),
                Position: Safe(cols, iPos).ToUpperInvariant(),
                College: Safe(cols, iCollege),
                GsisId: Safe(cols, iGsis)));
        }

        return rows;
    }

    private static string Safe(string[] cols, int idx) =>
        idx >= 0 && idx < cols.Length
            ? cols[idx].Trim().Trim('"')
            : string.Empty;

    private static string NormalizeName(string name) =>
        new string([.. name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ')]).Trim();

    private record DraftPickRow(
        int Round, int Pick, string PlayerName,
        string Position, string College, string GsisId);
}