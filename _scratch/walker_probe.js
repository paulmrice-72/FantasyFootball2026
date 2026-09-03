// _scratch/walker_probe.js
//
// Read-only. Writes nothing. Answers, in one pass:
//   0. which collections exist in this database, and how big they are
//   1. what PlayerGameLogs actually contains, by season
//   2. every "Walker" in the game logs, with the name nflverse gave us
//   3. which seasons ever got season-average sims seeded
//   4. every sim row for BOTH Kenneth Walkers (8151 = your RB, 4634 = the WR)
//   5. the 2026 projection inventory by week and basis
//   6. whether decimals are stored as Decimal128 or still as strings (FAN-129)
//
// Run against PROD over the tunnel:
//   mongosh "mongodb://admin:<pw>@127.0.0.1:27018/FFAnalytics?authSource=admin" _scratch/walker_probe.js > _scratch/walker_probe.json
//
// Or paste the body into Compass's embedded mongosh while connected to prod.

const out = { db: db.getName(), ranAt: new Date().toISOString() };

// ── 0: collection inventory ───────────────────────────────────────────────
out.collections = db.getCollectionNames().sort().map(function (c) {
  return { name: c, count: db.getCollection(c).estimatedDocumentCount() };
});

// ── 1: PlayerGameLogs by season / season type ─────────────────────────────
// If 2025 is missing or tiny here, the stats sync never ran in this
// environment and the name-matching theory is moot — nothing to match.
out.gameLogsBySeason = db.PlayerGameLogs.aggregate([
  { $group: {
      _id: { season: "$Season", seasonType: "$SeasonType" },
      docs: { $sum: 1 },
      players: { $addToSet: "$SleeperPlayerId" },
      withSleeperId: { $sum: { $cond: [
        { $gt: [ { $strLenCP: { $ifNull: ["$SleeperPlayerId", ""] } }, 0 ] }, 1, 0 ] } }
  } },
  { $project: { _id: 0,
      season: "$_id.season", seasonType: "$_id.seasonType",
      docs: 1, withSleeperId: 1, distinctPlayers: { $size: "$players" } } },
  { $sort: { season: -1, seasonType: 1 } }
]).toArray();

// ── 2: every Walker in the game logs, as nflverse names them ──────────────
// Broad regex on purpose — /walk/i catches "Ken Walker III" and any other
// variant. PlayerId is the gsis id; sleeperIds shows whether the backfill bound
// him. An empty sleeperIds ([] or [null]) means the gsis -> sleeper bridge missed.
out.walkerGameLogs = db.PlayerGameLogs.aggregate([
  { $match: { $or: [ { PlayerName: /walk/i }, { DisplayName: /walk/i } ] } },
  { $group: {
      _id: { gsis: "$PlayerId", playerName: "$PlayerName",
             displayName: "$DisplayName", position: "$Position",
             season: "$Season" },
      sleeperIds: { $addToSet: "$SleeperPlayerId" },
      teams: { $addToSet: "$NflTeam" },
      weeks: { $sum: 1 }
  } },
  { $project: { _id: 0,
      gsis: "$_id.gsis", playerName: "$_id.playerName",
      displayName: "$_id.displayName", position: "$_id.position",
      season: "$_id.season", sleeperIds: 1, teams: 1, weeks: 1 } },
  { $sort: { displayName: 1, season: -1 } }
]).toArray();

// ── 3: which seasons were ever seeded with season averages ────────────────
out.seasonAverageSims = db.simulation_results.aggregate([
  { $match: { Week: 0 } },
  { $group: { _id: "$Season", rows: { $sum: 1 } } },
  { $project: { _id: 0, season: "$_id", rows: 1 } },
  { $sort: { season: -1 } }
]).toArray();

// ── 4: every sim row for both Kenneth Walkers, any season/week ────────────
out.walkerSims = db.simulation_results.find(
  { SleeperPlayerId: { $in: ["8151", "4634"] } },
  { _id: 0, SleeperPlayerId: 1, PlayerName: 1, Position: 1, NflTeam: 1,
    Season: 1, Week: 1, Median: 1, PlayerRole: 1 }
).sort({ Season: -1, Week: -1 }).toArray();

// ── 5: 2026 projections by week and basis ─────────────────────────────────
// Expect Week 0 to be mostly PriorSeasonCarryover. Anything in Week 1 that
// is NOT RookieProjection would contradict what the Start/Sit card shows.
out.projections2026 = db.player_projections.aggregate([
  { $match: { Season: 2026 } },
  { $group: { _id: { week: "$Week", basis: "$Basis" }, rows: { $sum: 1 } } },
  { $project: { _id: 0, week: "$_id.week", basis: "$_id.basis", rows: 1 } },
  { $sort: { week: 1, basis: 1 } }
]).toArray();

// ── 6: FAN-129 — did the decimal migration actually run in this env? ──────
out.decimalTypes = ["player_projections", "simulation_results"].map(function (name) {
  const doc = db.getCollection(name).findOne({}, { Median: 1, ProjectedPointsHalfPpr: 1 });
  if (!doc) return { collection: name, sample: "none" };
  const field = name === "simulation_results" ? "Median" : "ProjectedPointsHalfPpr";
  return { collection: name, field: field, bsonType: typeof doc[field],
           value: String(doc[field]) };
});

print(JSON.stringify(out, null, 2));
