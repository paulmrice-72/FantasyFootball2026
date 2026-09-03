// _scratch/fix_mismatched_seasonavg.js
//
// Removes season-average sim rows that were bound to the WRONG Sleeper id by the
// old name-only matcher in SeedSeasonAverageSimsCommandHandler.
//
// Confirmed case (dev, 2026-09-02):
//   simulation_results { Season: 2024, Week: 0, SleeperPlayerId: "4634",
//                        PlayerName: "Kenneth Walker", Position: "RB",
//                        NflTeam: "SEA", Median: 14.38 }
//   4634 is a WIDE RECEIVER. Those are the running back's numbers (8151).
//   Prod wrote the same row onto 8151 correctly. Same code, same input file,
//   different winner — GroupBy(name).First() over an unordered GetAllAsync().
//
// The general detector: a Week 0 SeasonAverage row whose Position disagrees with
// the Position on the Players row for that SleeperPlayerId. Those can only have
// come from a bad bind, because the seed stamps the position from the STAT row
// while the id came from the name lookup.
//
// DRY RUN BY DEFAULT. Read the report, then flip DRY_RUN to false.
//
//   mongosh --quiet --host 127.0.0.1 --port 27017 --username admin \
//           --authenticationDatabase admin --password '<dev pw>' \
//           fantasycombine _scratch/fix_mismatched_seasonavg.js
//
// Positions live in POSTGRES, not Mongo, so this script cannot join to them.
// Paste the id->position pairs from the pgAdmin export into PLAYER_POSITIONS
// below, or leave it empty to run in TARGETED mode against KNOWN_BAD only.

const DRY_RUN = true;

// Targeted mode — ids/rows we have already proven wrong.
const KNOWN_BAD = [
  { SleeperPlayerId: "4634", Season: 2024, Week: 0, reason: "RB season average bound to WR 4634; correct id is 8151" }
];

// Optional sweep mode — { "<sleeperId>": "<POSITION>" } from Postgres.
const PLAYER_POSITIONS = {};

const out = { db: db.getName(), dryRun: DRY_RUN, ranAt: new Date().toISOString() };

// ── Targeted ───────────────────────────────────────────────────────────────
out.targeted = KNOWN_BAD.map(function (t) {
  const filter = { SleeperPlayerId: t.SleeperPlayerId, Season: t.Season, Week: t.Week };
  const found = db.simulation_results.find(filter).toArray();

  let deleted = 0;
  if (!DRY_RUN && found.length > 0) {
    deleted = db.simulation_results.deleteMany(filter).deletedCount;
  }

  return {
    filter: filter,
    reason: t.reason,
    matched: found.length,
    rows: found.map(function (r) {
      return { PlayerName: r.PlayerName, Position: r.Position,
               NflTeam: r.NflTeam, Median: String(r.Median), PlayerRole: r.PlayerRole };
    }),
    deleted: deleted
  };
});

// ── Sweep ──────────────────────────────────────────────────────────────────
if (Object.keys(PLAYER_POSITIONS).length > 0) {
  const suspects = [];

  db.simulation_results.find(
    { Week: 0, PlayerRole: "SeasonAverage" },
    { SleeperPlayerId: 1, PlayerName: 1, Position: 1, NflTeam: 1, Season: 1, Median: 1 }
  ).forEach(function (r) {
    const expected = PLAYER_POSITIONS[r.SleeperPlayerId];
    if (expected && r.Position && expected !== r.Position) {
      suspects.push({
        _id: r._id, SleeperPlayerId: r.SleeperPlayerId, Season: r.Season,
        PlayerName: r.PlayerName, rowPosition: r.Position,
        playerTablePosition: expected, NflTeam: r.NflTeam, Median: String(r.Median)
      });
    }
  });

  out.sweep = { count: suspects.length, suspects: suspects, deleted: 0 };

  if (!DRY_RUN && suspects.length > 0) {
    out.sweep.deleted = db.simulation_results.deleteMany(
      { _id: { $in: suspects.map(function (s) { return s._id; }) } }
    ).deletedCount;
  }
} else {
  out.sweep = "skipped — PLAYER_POSITIONS empty (targeted mode only)";
}

// Re-seeding the affected seasons after this is what puts the rows back on the
// correct ids. Deleting alone leaves a hole; that is deliberate, because a hole
// is now reported honestly by the depth-grade handler.
out.nextStep = "Re-run POST /api/v1/admin/jobs/seed-season-averages for each affected season " +
               "AFTER deploying the hardened matcher, then confirm MatchedByGsis > 0 in the response.";

print(JSON.stringify(out, null, 2));
