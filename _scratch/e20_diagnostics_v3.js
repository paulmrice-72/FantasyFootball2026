// ─────────────────────────────────────────────────────────────────────────────
// Replaces queries 3A and 3B — both of the originals were broken.
// Run in mongosh against DEV. Section 0 first; it decides everything else.
// ─────────────────────────────────────────────────────────────────────────────


// ═════════════════════════════════════════════════════════════════════════════
// 0. THE IMPORTANT ONE — what BSON type are the decimal fields actually stored as?
//
// 3A returned atFloor:0 AND atCeiling:1188 (every row). That combination is
// impossible for real numbers: with values spanning 0.75-1.35, some must be at
// or below 0.90. It IS what happens if the field is a STRING, because in BSON
// sort order every string ranks above every number — so `$gte 1.15` is true for
// everything and `$lte 0.90` is false for everything.
//
// The likely cause: the MongoDB C# driver serialises `decimal` as a STRING by
// default. Storing it as a number requires
// [BsonRepresentation(BsonType.Decimal128)] on the property.
//
// If this comes back "string", it is a real and pre-existing bug — see section 3.
// ═════════════════════════════════════════════════════════════════════════════

db.player_projections.aggregate([
  { $match: { Season: 2026 } },
  { $group: {
      _id: {
        usageTrend:  { $type: "$UsageTrendMultiplier" },
        halfPpr:     { $type: "$ProjectedPointsHalfPpr" },
        statLineTgt: { $type: "$StatLine.Targets" }
      },
      rows: { $sum: 1 }
  }}
]);

db.simulation_results.aggregate([
  { $match: { Season: 2026 } },
  { $group: {
      _id: {
        median: { $type: "$Median" },
        floor:  { $type: "$Floor" },
        base:   { $type: "$BaseProjection" }
      },
      rows: { $sum: 1 }
  }}
]);


// ═════════════════════════════════════════════════════════════════════════════
// 1. 3A REDONE — $toDouble, and filtered to ONE week
//
// Two things were wrong with the original:
//   a) no week filter. 1188 rows = 476 stale Week-0 rows from the pre-rookie run
//      + 712 Week-1 rows from the current build. The 0.75 / 1.35 values are the
//      OLD ones surviving in Week 0, which was never re-run.
//   b) numeric comparison against a possibly-string field (see section 0).
//
// $toDouble works whether the field is a string, a decimal or a double.
// ═════════════════════════════════════════════════════════════════════════════

db.player_projections.aggregate([
  { $match: { Season: 2026, Week: 1 } },       // <-- the freshly-run week only
  { $project: {
      Basis: 1,
      utm: { $toDouble: "$UsageTrendMultiplier" }
  }},
  { $group: {
      _id: "$Basis",
      rows:      { $sum: 1 },
      min:       { $min: "$utm" },
      max:       { $max: "$utm" },
      avg:       { $avg: "$utm" },
      atFloor:   { $sum: { $cond: [{ $lte: ["$utm", 0.9001] }, 1, 0] } },
      atCeiling: { $sum: { $cond: [{ $gte: ["$utm", 1.1499] }, 1, 0] } },
      belowOldFloor: { $sum: { $cond: [{ $lt: ["$utm", 0.90] }, 1, 0] } },
      aboveOldCeil:  { $sum: { $cond: [{ $gt: ["$utm", 1.15] }, 1, 0] } }
  }},
  { $sort: { _id: 1 } }
]);

// PASS looks like: min >= 0.90, max <= 1.15, belowOldFloor = 0, aboveOldCeil = 0,
// and RookieProjection rows sitting at exactly 1.0 (the prior sets no trend).
// FAIL (min 0.75 / max 1.35) means the recalibrated build isn't what produced
// these rows — rebuild and re-run week 1.


// ═════════════════════════════════════════════════════════════════════════════
// 2. 3B REDONE — the original returned nothing because $subtract on string
//    fields produces no usable result, so the `lower > 0` stage filtered
//    everything out. Cast first, then subtract.
// ═════════════════════════════════════════════════════════════════════════════

db.simulation_results.aggregate([
  { $match: { Season: 2026, Week: 1, Position: { $in: ["QB","RB","WR","TE"] } } },
  { $project: {
      Position: 1, PlayerName: 1,
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
  { $group: {
      _id: "$Position",
      n: { $sum: 1 },
      skew: { $avg: { $divide: ["$upper", "$lower"] } }
  }},
  { $sort: { _id: 1 } }
]);

// PASS: skew 1.3-1.5 — the stat-line Monte Carlo produces a right-skewed
// distribution. ~1.0 means it fell through to the legacy Gaussian path, i.e.
// StatLine came back null.


// ═════════════════════════════════════════════════════════════════════════════
// 3. IF SECTION 0 SAYS "string" — how much does it actually break?
//
// The C# side is FINE: the driver deserialises the string back to decimal, so
// every in-app calculation (grades, projections, LINQ comparisons) is correct.
// What breaks is anything the SERVER evaluates: sorts, range filters, aggregations.
//
// The one that matters: SortByDescending on a string field is LEXICOGRAPHIC.
//   "9.5" sorts ABOVE "17.4"  — a single-digit score outranks a double-digit one.
//
// Two repositories do exactly this:
//   PlayerProjectionRepository.GetByPositionAsync   -> SortByDescending(ProjectedPointsHalfPpr)
//   SimulationResultRepository.GetByPositionAsync   -> SortByDescending(Median)
//
// This query shows whether any page using those is mis-ordered right now:
// ═════════════════════════════════════════════════════════════════════════════

db.simulation_results
  .find({ Season: 2026, Week: 1, Position: "WR" }, { PlayerName: 1, Median: 1, _id: 0 })
  .sort({ Median: -1 })
  .limit(15);

// If the top of that list is players in the 9.x range while genuine 15-20 point
// WRs sit further down, the sort is lexicographic and every consumer of
// GetByPositionAsync has been showing a wrong order.
//
// Fix would be [BsonRepresentation(BsonType.Decimal128)] on the decimal
// properties plus a one-off migration to convert existing string values —
// worth its own ticket, and NOT something to change casually, since the
// migration has to run before the code change lands or reads will break.
