// ─────────────────────────────────────────────────────────────────────────────
// E20 post-rerun verification — ONE script, ONE export.
// Run in Compass's embedded mongosh against DEV (`fantasycombine`).
// Then: copy the printed JSON and save it as _scratch/verify_0902.json
//
// Everything below builds a single object and prints it at the end, so there is
// exactly one artifact to hand back — no crossed exports this time.
// ─────────────────────────────────────────────────────────────────────────────

const SEASON = 2026;
const out = {};

// ── 0. BSON storage types ────────────────────────────────────────────────────
// Decides whether server-side sorts/ranges are trustworthy at all.
// "string" here means the C# driver is serialising decimal as text, and every
// SortByDescending in a Mongo repository is lexicographic ("9.5" > "17.4").
out.types = {
  projections: db.player_projections.aggregate([
    { $match: { Season: SEASON } },
    { $group: {
        _id: {
          usageTrend:  { $type: "$UsageTrendMultiplier" },
          halfPpr:     { $type: "$ProjectedPointsHalfPpr" },
          ppr:         { $type: "$ProjectedPointsPpr" },
          statLineTgt: { $type: "$StatLine.Targets" }
        },
        rows: { $sum: 1 }
    }}
  ]).toArray(),

  simulations: db.simulation_results.aggregate([
    { $match: { Season: SEASON } },
    { $group: {
        _id: {
          median: { $type: "$Median" },
          floor:  { $type: "$Floor" },
          base:   { $type: "$BaseProjection" }
        },
        rows: { $sum: 1 }
    }}
  ]).toArray()
};

// ── 1. Inventory: what weeks/bases actually exist ────────────────────────────
// Confirms the re-run wrote Week 1 and tells us whether stale Week-0 rows are
// still sitting underneath (they shadow reads in some code paths).
out.inventory = db.player_projections.aggregate([
  { $match: { Season: SEASON } },
  { $group: { _id: { week: "$Week", basis: "$Basis" }, rows: { $sum: 1 } } },
  { $sort: { "_id.week": 1, "_id.basis": 1 } }
]).toArray();

// ── 2. Usage-trend multiplier, Week 1 only, cast to double ───────────────────
// PASS: min >= 0.90, max <= 1.15, belowFloor = 0, aboveCeil = 0,
//       RookieProjection rows pinned at exactly 1.0.
// FAIL (0.75 / 1.35): these rows predate the recalibration.
out.usageTrend = db.player_projections.aggregate([
  { $match: { Season: SEASON, Week: 1 } },
  { $project: { Basis: 1, utm: { $toDouble: "$UsageTrendMultiplier" } } },
  { $group: {
      _id: "$Basis",
      rows:       { $sum: 1 },
      min:        { $min: "$utm" },
      max:        { $max: "$utm" },
      avg:        { $avg: "$utm" },
      belowFloor: { $sum: { $cond: [{ $lt: ["$utm", 0.90] }, 1, 0] } },
      aboveCeil:  { $sum: { $cond: [{ $gt: ["$utm", 1.15] }, 1, 0] } },
      pinnedAt1:  { $sum: { $cond: [{ $eq: ["$utm", 1.0] }, 1, 0] } }
  }},
  { $sort: { _id: 1 } }
]).toArray();

// ── 3. Simulation skew by position ───────────────────────────────────────────
// PASS: 1.3-1.5 (right-skewed, stat-line Monte Carlo).
// ~1.0 means it fell through to the legacy Gaussian path — StatLine was null.
out.simSkew = db.simulation_results.aggregate([
  { $match: { Season: SEASON, Week: 1, Position: { $in: ["QB","RB","WR","TE"] } } },
  { $project: {
      Position: 1,
      median:  { $toDouble: "$Median" },
      floor:   { $toDouble: "$Floor" },
      ceiling: { $toDouble: "$Ceiling" }
  }},
  { $match: { median: { $gt: 5 } } },
  { $project: {
      Position: 1,
      upper: { $subtract: ["$ceiling", "$median"] },
      lower: { $subtract: ["$median", "$floor"] }
  }},
  { $match: { lower: { $gt: 0 } } },
  { $group: { _id: "$Position", n: { $sum: 1 }, skew: { $avg: { $divide: ["$upper", "$lower"] } } } },
  { $sort: { _id: 1 } }
]).toArray();

// ── 4. Lexicographic-sort probe ──────────────────────────────────────────────
// serverSorted uses .sort({Median:-1}) exactly as the repositories do.
// clientSorted casts first and is the truth. If the two lists disagree,
// every consumer of GetByPositionAsync has been rendering a wrong order.
out.sortProbe = {
  serverSorted: db.simulation_results
    .find({ Season: SEASON, Week: 1, Position: "WR" }, { PlayerName: 1, Median: 1, _id: 0 })
    .sort({ Median: -1 }).limit(15).toArray(),

  clientSorted: db.simulation_results.aggregate([
    { $match: { Season: SEASON, Week: 1, Position: "WR" } },
    { $project: { _id: 0, PlayerName: 1, m: { $toDouble: "$Median" } } },
    { $sort: { m: -1 } },
    { $limit: 15 }
  ]).toArray()
};

// ── 5. Zero / missing-projection census  (feeds FAN-124) ─────────────────────
// How many rows would render a hard "0.0" today, by position.
out.zeroCensus = db.player_projections.aggregate([
  { $match: { Season: SEASON, Week: 1 } },
  { $project: {
      Position: 1,
      Basis: 1,
      pts: { $toDouble: { $ifNull: ["$ProjectedPointsHalfPpr", 0] } },
      hasStatLine: { $cond: [{ $eq: [{ $type: "$StatLine" }, "missing"] }, 0, 1] }
  }},
  { $group: {
      _id: { pos: "$Position", basis: "$Basis" },
      rows:        { $sum: 1 },
      zeroPts:     { $sum: { $cond: [{ $lte: ["$pts", 0.0001] }, 1, 0] } },
      noStatLine:  { $sum: { $cond: [{ $eq: ["$hasStatLine", 0] }, 1, 0] } }
  }},
  { $sort: { "_id.pos": 1, "_id.basis": 1 } }
]).toArray();

// ── 6. Rostered players with no projection at all ────────────────────────────
// The population that actually shows a 0.0 on a roster page.
out.rosteredUnprojected = db.simulation_results.aggregate([
  { $match: { Season: SEASON, Week: 1 } },
  { $project: {
      _id: 0, PlayerName: 1, Position: 1,
      m: { $toDouble: { $ifNull: ["$Median", 0] } }
  }},
  { $match: { m: { $lte: 0.0001 } } },
  { $group: { _id: "$Position", n: { $sum: 1 }, sample: { $push: "$PlayerName" } } },
  { $project: { _id: 1, n: 1, sample: { $slice: ["$sample", 8] } } },
  { $sort: { _id: 1 } }
]).toArray();

print(JSON.stringify(out, null, 2));
