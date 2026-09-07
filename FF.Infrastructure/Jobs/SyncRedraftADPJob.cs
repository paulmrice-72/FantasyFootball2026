// FF.Infrastructure/Jobs/SyncRedraftAdpJob.cs
using FF.Application.Interfaces.External;
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Infrastructure.Persistence.Mongo;
using Hangfire;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Jobs;

public class SyncRedraftAdpJob(
    IFantasyFootballCalculatorService ffcService,
    IPlayerRepository playerRepository,
    MongoDbContext db,
    ILogger<SyncRedraftAdpJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var season = DateTime.UtcNow.Month >= 3
            ? DateTime.UtcNow.Year
            : DateTime.UtcNow.Year - 1;

        logger.LogInformation("SyncRedraftAdpJob starting for season {Season}", season);

        // ── 1. Fetch ADP from FFC ────────────────────────────────────────────
        var adpEntries = await ffcService.GetAdpAsync(season, "ppr", 12, ct);
        if (adpEntries.Count == 0)
        {
            logger.LogWarning("SyncRedraftAdpJob: FFC returned no data — skipping upsert");
            return;
        }

        // ── 2. Build name lookup from dynasty_valuations ─────────────────────
        // Fields are PascalCase: PlayerName, SleeperPlayerId, Position
        var valuationCollection = db.GetCollection<PlayerLookup>("dynasty_valuations");
        var players = await valuationCollection
            .Find(FilterDefinition<PlayerLookup>.Empty)
            .Project(p => new PlayerLookup
            {
                SleeperPlayerId = p.SleeperPlayerId,
                PlayerName = p.PlayerName,
                Position = p.Position,
                NflTeam = p.NflTeam
            })
            .ToListAsync(ct);

        // Deduplicate — multiple seasons per player; one entry is enough for name matching
        var deduped = players
            .Where(p => !string.IsNullOrEmpty(p.PlayerName) && !string.IsNullOrEmpty(p.SleeperPlayerId))
            // Sleeper's placeholder rows are not people. Left in, they can absorb
            // a real ADP entry — the same rows that once carried a TradeValue of
            // 81.8 on the dynasty board.
            .Where(p => !PlayerNameNormalizer.IsPlaceholder(p.PlayerName))
            .GroupBy(p => p.SleeperPlayerId!)
            .Select(g => g.First())
            .ToList();

        var nameLookup = deduped
            .GroupBy(p => NormalizeName(p.PlayerName!))
            .ToDictionary(g => g.Key, g => g.ToList());

        logger.LogInformation("SyncRedraftAdpJob: {Count} unique players in name lookup", deduped.Count);

        // ── 2b. Kickers and defenses — a second id source ────────────────────
        //
        // 2026-09-07. The lookup above is built from `dynasty_valuations`, and
        // the dynasty pipeline only ever scores QB/RB/WR/TE. So even with the
        // K/DST skip removed (see below) every kicker and defense would fall
        // straight into `unmatched`: there is no row to match against.
        //
        // The Players table does hold them — SleeperPlayerMapper.MapPosition has
        // mapped "K" and "DEF" since the beginning and SleeperPlayerSyncService
        // writes the full universe — so resolve these two positions from there.
        //
        // Defenses are matched by TEAM, not by name. Sleeper's player_id for a
        // team defense IS the team abbreviation ("PHI"), while FFC publishes the
        // nickname alone ("Eagles"). Name matching between those two is a coin
        // flip; the team code is exact.
        var kickers = await playerRepository.GetByPositionAsync(Position.K, ct);
        var defenses = await playerRepository.GetByPositionAsync(Position.DEF, ct);

        var kickerLookup = kickers
            .Where(p => !string.IsNullOrEmpty(p.SleeperPlayerId))
            .GroupBy(p => NormalizeName(p.FullName))
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => new PlayerLookup
                {
                    SleeperPlayerId = p.SleeperPlayerId,
                    PlayerName = p.FullName,
                    Position = "K",
                    NflTeam = p.NflTeam
                }).ToList());

        var defenseByTeam = defenses
            .Where(p => !string.IsNullOrEmpty(p.SleeperPlayerId) && !string.IsNullOrEmpty(p.NflTeam))
            .GroupBy(p => p.NflTeam!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new PlayerLookup
                {
                    SleeperPlayerId = g.First().SleeperPlayerId,
                    PlayerName = g.First().FullName,
                    Position = "DEF",
                    NflTeam = g.Key
                },
                StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "SyncRedraftAdpJob: {Kickers} kickers and {Defenses} defenses available from the Players table",
            kickerLookup.Count, defenseByTeam.Count);

        // ── 3. Upsert into redraftAdpCache ───────────────────────────────────
        var adpCollection = db.GetCollection<RedraftAdpCacheDocument>("redraftAdpCache");

        int matched = 0, unmatched = 0;

        // Counted separately and logged below on purpose: these two positions
        // were absent from this cache entirely until 2026-09-07, so "did the K
        // and DEF fix actually take" is a question the run log should answer
        // without anyone having to open Compass.
        int matchedKickers = 0, matchedDefenses = 0;

        var unmatchedByPosition = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Stale-entry cleanup (2026-08-30, FAN-105 follow-up): a player who
        // matched in a prior run but drops out of FFC's feed — or stops
        // matching by name — used to leave a permanent zombie document
        // behind (no upsert ever touches it again, so it just sits at
        // whatever ADP/team it last had, sometimes months stale). Track
        // everyone who matches THIS run and prune anything else for this
        // Season/ScoringFormat afterward, so the cache always reflects only
        // the current FFC pull.
        var matchedSleeperIds = new HashSet<string>();

        // Position decides which id source can answer. Kept as one function so
        // that "which lookup answers for this position" is a single readable
        // decision rather than three branches scattered through the loop.
        PlayerLookup? ResolveMatch(FfcPlayerAdp entry)
        {
            var normalizedName = NormalizeName(entry.Name);

            if (string.Equals(entry.Position, "DEF", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrEmpty(entry.Team)
                       && defenseByTeam.TryGetValue(entry.Team, out var def)
                    ? def
                    : null;
            }

            if (string.Equals(entry.Position, "K", StringComparison.OrdinalIgnoreCase))
            {
                return kickerLookup.TryGetValue(normalizedName, out var kickers)
                    ? kickers.FirstOrDefault(k =>
                          string.IsNullOrEmpty(entry.Team)
                          || string.Equals(k.NflTeam, entry.Team, StringComparison.OrdinalIgnoreCase))
                      ?? kickers.First()
                    : null;
            }

            if (!nameLookup.TryGetValue(normalizedName, out var candidates))
                return null;

            return candidates.FirstOrDefault(c =>
                       string.Equals(c.Position, entry.Position, StringComparison.OrdinalIgnoreCase))
                   ?? candidates.First();
        }

        foreach (var entry in adpEntries)
        {
            // 2026-09-07. This loop used to open with:
            //
            //     if (entry.Position is "K" or "DST") continue;
            //
            // FFC publishes kicker and defense ADP, this service normalises both
            // correctly, and that one line threw them away before they could
            // reach `redraftAdpCache`. Since the draft board's player pool IS
            // that cache, a league requiring a K and a DEF could not be shown a
            // single one of either — for the whole draft, with no error and no
            // empty-state, because from the board's point of view the positions
            // simply did not exist.
            PlayerLookup? match = ResolveMatch(entry);

            if (match is null)
            {
                logger.LogDebug("No match: {Name} ({Position})", entry.Name, entry.Position);
                unmatched++;

                // 2026-09-07: tally the POSITION TOKENS that fail, not just the
                // count. The first live run reported "0 kickers matched" and
                // suggested checking whether PlayerSyncJob had run — while the
                // line directly above it said 192 kickers were available. The
                // real cause was that FFC calls them "PK", so nothing was ever
                // looked up as a kicker. A per-token tally says that outright.
                unmatchedByPosition[entry.Position] =
                    unmatchedByPosition.TryGetValue(entry.Position, out var u) ? u + 1 : 1;

                continue;
            }

            var filter = Builders<RedraftAdpCacheDocument>.Filter.And(
                Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.SleeperPlayerId, match.SleeperPlayerId),
                Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.Season, season),
                Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.ScoringFormat, "ppr"));

            var update = Builders<RedraftAdpCacheDocument>.Update
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .Set(x => x.SleeperPlayerId, match.SleeperPlayerId!)
                // Defenses take the Sleeper name ("Philadelphia Eagles"), not
                // FFC's nickname ("Eagles"), so the board's ADP row and the
                // drafted-pick row for the same defense read as the same thing.
                .Set(x => x.PlayerName,
                    string.Equals(entry.Position, "DEF", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(match.PlayerName)
                        ? match.PlayerName!
                        : entry.Name)
                .Set(x => x.Position, entry.Position)
                // FFC's own ADP feed leaves "team" null/empty for a chunk of
                // established veterans (offseason data gap on their end) —
                // fall back to the roster team we already have on file from
                // dynasty_valuations (kept current by PlayerSyncJob) rather
                // than showing those players as free agents.
                .Set(x => x.NflTeam, string.IsNullOrEmpty(entry.Team)
                    ? (string.IsNullOrEmpty(match.NflTeam) ? null : match.NflTeam)
                    : entry.Team)
                .Set(x => x.Adp, entry.Adp)
                .Set(x => x.AdpRound, entry.AdpRound)
                .Set(x => x.Season, season)
                .Set(x => x.ScoringFormat, "ppr")
                .Set(x => x.TeamCount, 12)
                .Set(x => x.SyncedAt, DateTime.UtcNow);

            await adpCollection.UpdateOneAsync(filter, update,
                new UpdateOptions { IsUpsert = true }, ct);

            matched++;
            matchedSleeperIds.Add(match.SleeperPlayerId!);

            if (string.Equals(entry.Position, "K", StringComparison.OrdinalIgnoreCase)) matchedKickers++;
            else if (string.Equals(entry.Position, "DEF", StringComparison.OrdinalIgnoreCase)) matchedDefenses++;
        }

        // ── 4. Prune stale entries not matched this run ──────────────────────
        var staleFilter = Builders<RedraftAdpCacheDocument>.Filter.And(
            Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.Season, season),
            Builders<RedraftAdpCacheDocument>.Filter.Eq(x => x.ScoringFormat, "ppr"),
            Builders<RedraftAdpCacheDocument>.Filter.Nin(x => x.SleeperPlayerId, matchedSleeperIds));

        var pruneResult = await adpCollection.DeleteManyAsync(staleFilter, ct);

        logger.LogInformation(
            "SyncRedraftAdpJob complete: {Matched} matched ({Kickers} K, {Defenses} DEF), " +
            "{Unmatched} unmatched of {Total}, {Pruned} stale entries pruned",
            matched, matchedKickers, matchedDefenses, unmatched, adpEntries.Count,
            pruneResult.DeletedCount);

        if (unmatchedByPosition.Count > 0)
        {
            logger.LogInformation(
                "SyncRedraftAdpJob: unmatched by position token — {Breakdown}",
                string.Join(", ", unmatchedByPosition
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}={kv.Value}")));
        }

        if (matchedKickers == 0 || matchedDefenses == 0)
        {
            logger.LogWarning(
                "SyncRedraftAdpJob: {Kickers} kickers and {Defenses} defenses matched " +
                "against {AvailableKickers} kickers and {AvailableDefenses} defenses in the " +
                "Players table. Both should be non-zero — a league that starts a K or DEF " +
                "cannot see any on the draft board while this is 0. If the Players table has " +
                "rows but nothing matched, the position TOKEN is the likely cause (FFC calls " +
                "kickers \"PK\", Sleeper calls them \"K\") — see the unmatched-by-token line " +
                "above. Otherwise check that PlayerSyncJob has run.",
                matchedKickers, matchedDefenses, kickerLookup.Count, defenseByTeam.Count);
        }
    }

    // 2026-09-07 (FAN-156): this job kept its own private NormalizeName — a THIRD
    // copy of a rule that is supposed to have one home. It stripped a different
    // suffix list, had no placeholder detection (so Sleeper's "Duplicate Player"
    // rows could match), and folded no diacritics, which is what left Eddy
    // Piñeiro unmatched. Deleted in favour of the shared normalizer; both sides
    // of every comparison in this job go through it, which is the only property
    // that actually matters.
    private static string NormalizeName(string name) =>
        PlayerNameNormalizer.Normalize(name);

    private class PlayerLookup
    {
        public string? SleeperPlayerId { get; set; }
        public string? PlayerName { get; set; }
        public string? Position { get; set; }
        public string? NflTeam { get; set; }
    }
}