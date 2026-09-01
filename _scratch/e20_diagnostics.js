// ─────────────────────────────────────────────────────────────────────────────
// Epic 20 diagnostics — 2026-09-01
// Run in MongoDB Compass (mongosh tab) against the DEV database.
// Export each result to _scratch/ as JSON and I'll read it off the folder.
//
// Section 1  FAN-122 — how bad is the duplicate-identity problem, really
// Section 2  Rookie prior — does the source data for 2026 actually exist
// Section 3  Post-rebuild verification — run after the next build + jobs
// ─────────────────────────────────────────────────────────────────────────────


// ═════════════════════════════════════════════════════════════════════════════
// 1. FAN-122 — DUPLICATE PLAYER IDENTITIES
// ═════════════════════════════════════════════════════════════════════════════

// 1a. Full scope, ALL positions. I could only see WR/TE in the export.
//     Any row returned is one human being stored under two or more PlayerIds.
//     → export as _scratch/dupe_identities.json
db.player_game_logs.aggregate([
  { $match: { Season: 2025 } },
  { $group: {
      _id:   { name: "$PlayerName", pos: "$Position" },
      ids:   { $addToSet: "$PlayerId" },
      sleeperIds: { $addToSet: "$SleeperPlayerId" },
      teams: { $addToSet: "$NflTeam" },
      rows:  { $sum: 1 }
  }},
  { $match: { "ids.1": { $exists: true } } },
  { $project: {
      _id: 0, name: "$_id.name", pos: "$_id.pos",
      idCount: { $size: "$ids" }, ids: 1, sleeperIds: 1, teams: 1, rows: 1
  }},
  { $sort: { idCount: -1, pos: 1, name: 1 } }
]);

// 1b. Is each identity a HALF of the season, or a near-complete DUPLICATE?
//     This decides the fix: merge-and-sum vs pick-canonical-and-drop.
//     If weeks overlap between the two ids → duplicate. If they partition → split.
//     → export as _scratch/dupe_breakdown.json
db.player_game_logs.aggregate([
  { $match: { Season: 2025, Position: { $in: ["QB","RB","WR","TE"] } } },
  { $group: {
      _id: { name: "$PlayerName", pos: "$Position", playerId: "$PlayerId" },
      sleeperId: { $addToSet: "$SleeperPlayerId" },
      games:   { $sum: 1 },
      weeks:   { $addToSet: "$Week" },
      targets: { $sum: "$Targets" },
      carries: { $sum: "$Carries" },
      recYds:  { $sum: "$ReceivingYards" }
  }},
  { $group: {
      _id: { name: "$_id.name", pos: "$_id.pos" },
      identities: { $push: {
          playerId: "$_id.playerId", sleeperId: "$sleeperId",
          games: "$games", weeks: "$weeks",
          targets: "$targets", carries: "$carries", recYds: "$recYds"
      }},
      n: { $sum: 1 }
  }},
  { $match: { n: { $gt: 1 } } },
  { $sort: { "_id.pos": 1, "_id.name": 1 } }
]);

// 1c. Do the duplicates share one SleeperPlayerId? That determines whether the
//     site currently shows one of them arbitrarily, or both as separate players.
db.player_game_logs.aggregate([
  { $match: { Season: 2025 } },
  { $group: { _id: "$SleeperPlayerId", playerIds: { $addToSet: "$PlayerId" } } },
  { $match: { "playerIds.1": { $exists: true }, _id: { $nin: [null, ""] } } },
  { $count: "sleeperIdsWithMultipleGsisIds" }
]);


// ═════════════════════════════════════════════════════════════════════════════
// 2. ROOKIE PRIOR — IS THE SOURCE DATA THERE FOR 2026?
//    This is what blocks the last open piece of FAN-116. A rookie with no NFL
//    logs currently gets no projection at all, which is why Omar Cooper is a 0.
//    I can build the prior, but not against collections I can't confirm exist.
// ═════════════════════════════════════════════════════════════════════════════

// 2a. Row counts by season for each source. Anything with no 2026 row is a gap.
db.pff_draft_grades.aggregate([
  { $group: { _id: "$Season", rows: { $sum: 1 },
              matched: { $sum: { $cond: [{ $gt: ["$SleeperPlayerId", ""] }, 1, 0] } } } },
  { $sort: { _id: -1 } }
]);

db.fantasyPros_rookie_rankings.aggregate([
  { $group: { _id: { season: "$Season", type: "$RankingType" }, rows: { $sum: 1 },
              matched: { $sum: { $cond: [{ $gt: ["$SleeperPlayerId", ""] }, 1, 0] } } } },
  { $sort: { "_id.season": -1 } }
]);

db.combine_results.aggregate([
  { $group: { _id: "$Season", rows: { $sum: 1 } } },
  { $sort: { _id: -1 } }
]);

// 2b. depth_charts — flagged as unconfirmed on 08-31 and never checked.
//     L0 volume wants this. Latest 10 season/week combinations:
db.depth_charts.aggregate([
  { $group: { _id: { season: "$Season", week: "$Week" }, rows: { $sum: 1 },
              teams: { $addToSet: "$NflTeam" } } },
  { $project: { _id: 0, season: "$_id.season", week: "$_id.week",
                rows: 1, teamCount: { $size: "$teams" } } },
  { $sort: { season: -1, week: -1 } },
  { $limit: 10 }
]);

// 2c. THE key question: how many 2026 rookies are on a roster in your leagues
//     but have no projection? These are the players currently rendering as 0.
//     → export as _scratch/unprojected_rostered.json
db.roster_players.aggregate([
  { $group: { _id: "$SleeperPlayerId", rosters: { $sum: 1 } } },
  { $lookup: {
      from: "player_projections",
      let: { sid: "$_id" },
      pipeline: [
        { $match: { $expr: { $and: [
            { $eq: ["$SleeperPlayerId", "$$sid"] },
            { $eq: ["$Season", 2026] }
        ]}}},
        { $limit: 1 }
      ],
      as: "proj"
  }},
  { $match: { proj: { $size: 0 } } },
  { $count: "rosteredWithNo2026Projection" }
]);
// NOTE: if the roster collection isn't named roster_players, run
//   db.getCollectionNames()
// and tell me the right one — I'll rewrite this.


// ═════════════════════════════════════════════════════════════════════════════
// 3. AFTER THE NEXT REBUILD + RE-RUN
//    Three changes are now in the tree and need a build:
//      · usage-trend recalibration     (StatLineProjectionService)
//      · per-player fallback + week-0   (SimulationResultRepository)
//      · stat-line Monte Carlo          (MonteCarloSimulationService)
// ═════════════════════════════════════════════════════════════════════════════

// 3a. Usage trend should no longer pile up at the clamps.
//     Before: 66 rows at 0.75 and 54 at 1.35 out of 584.
//     After:  nothing outside 0.90-1.15, and far fewer exactly at the edges.
db.player_projections.aggregate([
  { $match: { Season: 2026 } },
  { $group: {
      _id: null,
      rows: { $sum: 1 },
      atFloor:   { $sum: { $cond: [{ $lte: ["$UsageTrendMultiplier", 0.90] }, 1, 0] } },
      atCeiling: { $sum: { $cond: [{ $gte: ["$UsageTrendMultiplier", 1.15] }, 1, 0] } },
      min: { $min: "$UsageTrendMultiplier" },
      max: { $max: "$UsageTrendMultiplier" }
  }}
]);

// 3b. The new Monte Carlo should be RIGHT-SKEWED. The old one was symmetric by
//     construction. Expect skew ≈ 1.3-1.5; anything ≈ 1.0 means it fell through
//     to the legacy path, i.e. StatLine was null.
db.simulation_results.aggregate([
  { $match: { Season: 2026, Position: { $in: ["WR","TE","RB","QB"] }, Median: { $gt: 5 } } },
  { $project: {
      Position: 1, PlayerName: 1,
      upper: { $subtract: ["$Ceiling", "$Median"] },
      lower: { $subtract: ["$Median", "$Floor"] }
  }},
  { $match: { lower: { $gt: 0 } } },
  { $group: { _id: "$Position", n: { $sum: 1 },
              skew: { $avg: { $divide: ["$upper", "$lower"] } } } },
  { $sort: { _id: 1 } }
]);

// 3c. Week 0 should no longer be the only thing that resolves. Sanity check of
//     what exists per season/week after the re-run:
db.simulation_results.aggregate([
  { $group: { _id: { season: "$Season", week: "$Week" }, players: { $sum: 1 } } },
  { $sort: { "_id.season": -1, "_id.week": 1 } }
]);


// ═════════════════════════════════════════════════════════════════════════════
// 4. pgAdmin (Postgres, dev) — draft capital coverage for the rookie prior
// ═════════════════════════════════════════════════════════════════════════════
/*
SELECT
  "Position",
  COUNT(*)                                                   AS players,
  COUNT(*) FILTER (WHERE "DraftRound"    IS NOT NULL)        AS with_draft_round,
  COUNT(*) FILTER (WHERE "DraftPick"     IS NOT NULL)        AS with_draft_pick,
  COUNT(*) FILTER (WHERE "YearsExperience" = 0)              AS rookies,
  COUNT(*) FILTER (WHERE "YearsExperience" = 0
                     AND "DraftRound" IS NOT NULL)           AS rookies_with_capital
FROM "Players"
GROUP BY "Position"
ORDER BY "Position";
*/
// If rookies_with_capital is near zero, the draft-capital prior has nothing to
// stand on and the rookie projection has to lean on PFF grade + FantasyPros rank
// instead — which pushes it closer to consensus-mirroring than you wanted.
