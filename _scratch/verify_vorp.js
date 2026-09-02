// ─────────────────────────────────────────────────────────────────────────────
// FAN-118 / FAN-129 verification. Run in Compass's mongosh against DEV.
// Save the printed JSON as _scratch/verify_vorp.json
//
// The QB question is answered by "impliedStarters", NOT by the raw replacement
// number. Replacement level is just a points value — meaningless on its own. What
// tells you the league config was read correctly is HOW MANY quarterbacks project
// above it, because that is exactly the number of QB starting slots the engine
// believes your league has.
//
//   1QB, 12 teams        → impliedStarters.QB ≈ 12
//   Superflex, 12 teams  → impliedStarters.QB ≈ 24   ← the superflex signal
//
// If a superflex league reports ~12, RosterConfiguration is not coming through as
// superflex and the whole point of L3 is not being realised.
// ─────────────────────────────────────────────────────────────────────────────

const SEASON = 2026;
const WEEK   = 1;
const out    = {};

// ── 0. Did the migration actually land? ──────────────────────────────────────
// Should now read "decimal" everywhere, not "string". vorp_recommendations was
// empty at migration time, so its rows were written fresh by the tolerant
// serializer — if THAT one says decimal, the serializer is live in your build.
out.storageTypes = {
  projections: db.player_projections.aggregate([
    { $match: { Season: SEASON } },
    { $group: { _id: { halfPpr: { $type: "$ProjectedPointsHalfPpr" },
                       statLine: { $type: "$StatLine.Targets" } }, rows: { $sum: 1 } } }
  ]).toArray(),

  simulations: db.simulation_results.aggregate([
    { $match: { Season: SEASON } },
    { $group: { _id: { median: { $type: "$Median" } }, rows: { $sum: 1 } } }
  ]).toArray(),

  vorp: db.vorp_recommendations.aggregate([
    { $group: { _id: { vorp:  { $type: "$Vorp" },
                       repl:  { $type: "$ReplacementLevel" },
                       faVorp:{ $type: "$VorpFreeAgent" } }, rows: { $sum: 1 } } }
  ]).toArray()
};

// ── 1. Server-side sort now correct? ─────────────────────────────────────────
// These two lists must agree. Before the migration they did not.
out.sortProbe = {
  serverSorted: db.simulation_results
    .find({ Season: SEASON, Week: WEEK, Position: "WR" }, { PlayerName: 1, Median: 1, _id: 0 })
    .sort({ Median: -1 }).limit(10).toArray(),

  clientSorted: db.simulation_results.aggregate([
    { $match: { Season: SEASON, Week: WEEK, Position: "WR" } },
    { $project: { _id: 0, PlayerName: 1, m: { $toDouble: "$Median" } } },
    { $sort: { m: -1 } }, { $limit: 10 }
  ]).toArray()
};

// ── 2. What leagues have a board, and how big are they? ──────────────────────
out.leagues = db.vorp_recommendations.aggregate([
  { $match: { Season: SEASON, Week: WEEK } },
  { $group: {
      _id: "$SleeperLeagueId",
      rows:      { $sum: 1 },
      rostered:  { $sum: { $cond: ["$IsRostered", 1, 0] } },
      freeAgents:{ $sum: { $cond: ["$IsRostered", 0, 1] } },
      exhausted: { $sum: { $cond: ["$ReplacementPoolExhausted", 1, 0] } }
  }}
]).toArray();

// ── 3. THE ANSWER — implied starters per position, per league ────────────────
// impliedStarters = how many players at the position project strictly above the
// stored replacement level. That IS the engine's view of your league's starting
// slots at that position, flex allocation included.
out.impliedStarters = db.vorp_recommendations.aggregate([
  { $match: { Season: SEASON, Week: WEEK } },
  { $project: {
      SleeperLeagueId: 1, Position: 1,
      pts:  { $toDouble: "$ProjectedPoints" },
      repl: { $toDouble: "$ReplacementLevel" },
      faRepl: { $toDouble: "$ReplacementLevelFreeAgent" }
  }},
  { $group: {
      _id: { league: "$SleeperLeagueId", pos: "$Position" },
      poolSize:        { $sum: 1 },
      impliedStarters: { $sum: { $cond: [{ $gt: ["$pts", "$repl"] }, 1, 0] } },
      replacementLevel:{ $max: "$repl" },
      freeAgentLevel:  { $max: "$faRepl" },
      topProjection:   { $max: "$pts" }
  }},
  { $sort: { "_id.league": 1, "_id.pos": 1 } }
]).toArray();

// ── 4. The QB board, top 15 — eyeball test ───────────────────────────────────
// In superflex the QBs at the top should carry large positive Vorp, and the
// replacement-level QB should sit around rank 24-25, not 12-13.
out.qbBoard = db.vorp_recommendations.aggregate([
  { $match: { Season: SEASON, Week: WEEK, Position: "QB" } },
  { $project: {
      _id: 0, PlayerName: 1, IsRostered: 1,
      proj:   { $toDouble: "$ProjectedPoints" },
      vorp:   { $toDouble: "$Vorp" },
      faVorp: { $toDouble: "$VorpFreeAgent" },
      posRank: "$PositionRank"
  }},
  { $sort: { vorp: -1 } },
  { $limit: 15 }
]).toArray();

// ── 5. Do the two baselines actually differ? ─────────────────────────────────
// If structural == free-agent for every position, one of them is not being
// computed and the "store both" decision bought nothing.
out.baselineSpread = db.vorp_recommendations.aggregate([
  { $match: { Season: SEASON, Week: WEEK } },
  { $group: {
      _id: "$Position",
      structural: { $max: { $toDouble: "$ReplacementLevel" } },
      freeAgent:  { $max: { $toDouble: "$ReplacementLevelFreeAgent" } }
  }},
  { $project: {
      structural: 1, freeAgent: 1,
      gap: { $subtract: ["$structural", "$freeAgent"] }
  }},
  { $sort: { _id: 1 } }
]).toArray();

print(JSON.stringify(out, null, 2));
