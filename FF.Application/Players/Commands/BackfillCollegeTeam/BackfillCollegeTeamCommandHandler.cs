// FF.Application/Players/Commands/BackfillCollegeTeam/BackfillCollegeTeamCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Players.Commands.BackfillCollegeTeam;

public class BackfillCollegeTeamCommandHandler(
    IPlayerRepository playerRepository,
    ILogger<BackfillCollegeTeamCommandHandler> logger)
    : IRequestHandler<BackfillCollegeTeamCommand, Result<BackfillCollegeTeamResult>>
{
    public async Task<Result<BackfillCollegeTeamResult>> Handle(
        BackfillCollegeTeamCommand request,
        CancellationToken cancellationToken)
    {
        // 1 — Parse CSV into gsis_id → college lookup
        var lookup = ParseCsv(request.CsvContent);
        if (lookup.Count == 0)
            return Result<BackfillCollegeTeamResult>.Failure(
                new Error("BACKFILL_EMPTY", "CSV contained no usable gsis_id/college rows."));

        logger.LogInformation(
            "College backfill: parsed {Count} gsis_id→college entries from CSV", lookup.Count);

        // 2 — Load all players with a GsisId but no CollegeTeam
        var players = await playerRepository.GetPlayersNeedingCollegeBackfillAsync(cancellationToken);

        logger.LogInformation(
            "College backfill: {Count} players have GsisId but no CollegeTeam", players.Count);

        var updated = 0;
        var skipped = 0;

        foreach (var player in players)
        {
            if (player.GsisId is null) continue;

            if (!lookup.TryGetValue(player.GsisId, out var college)
                || string.IsNullOrWhiteSpace(college))
            {
                skipped++;
                continue;
            }

            player.UpdateDraftCapital(player.DraftRound, player.DraftPick, college);
            await playerRepository.UpdateAsync(player, cancellationToken);
            updated++;
        }

        var unmatchedInCsv = lookup.Count - updated;

        logger.LogInformation(
            "College backfill complete — Updated: {Updated}, Skipped: {Skipped}, CsvUnmatched: {Unmatched}",
            updated, skipped, unmatchedInCsv);

        return Result<BackfillCollegeTeamResult>.Success(
            new BackfillCollegeTeamResult(updated, skipped, unmatchedInCsv));
    }

    // Parses nflverse roster CSV — expects headers including gsis_id and college
    private static Dictionary<string, string> ParseCsv(string csv)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return lookup;

        var headers = lines[0].Split(',')
            .Select(h => h.Trim().Trim('"').ToLowerInvariant())
            .ToArray();

        var gsisIdx = Array.IndexOf(headers, "gsis_id");
        var collegeIdx = Array.IndexOf(headers, "college");

        if (gsisIdx < 0 || collegeIdx < 0) return lookup;

        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');
            if (cols.Length <= Math.Max(gsisIdx, collegeIdx)) continue;

            var gsisId = cols[gsisIdx].Trim().Trim('"');
            var college = cols[collegeIdx].Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(gsisId) || gsisId == "NA") continue;
            if (string.IsNullOrWhiteSpace(college) || college == "NA") continue;

            // Last one wins — roster_2025 will naturally override roster_2024
            lookup[gsisId] = college;
        }

        return lookup;
    }
}