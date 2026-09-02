// ─────────────────────────────────────────────────────────────────────────────
// FAN-129 — convert string-stored decimals to Decimal128.
//
// Run in Compass's embedded mongosh, against ONE environment at a time.
// Set DRY_RUN=true first, read the report, then set it false and re-run.
//
// SAFE TO RUN BEFORE OR AFTER the TolerantDecimalSerializer deploy, and safe to
// run twice — that is the whole point of the tolerant serializer. It reads
// String and Decimal128 alike, so there is no window where one side is wrong and
// no required ordering between this script and the code.
//
// It only ever rewrites a field that is CURRENTLY a string AND parses cleanly as
// a number. Anything else is counted and left alone.
// ─────────────────────────────────────────────────────────────────────────────

const DRY_RUN = true;          // ← flip to false to actually write
const BATCH   = 500;

// ── The field inventory ──────────────────────────────────────────────────────
// Derived from `public decimal` / `public decimal?` properties on the FF.Domain
// document types, mapped to the collection each repository actually opens.
//
// Matching is BY FIELD NAME, applied recursively through nested objects and
// arrays. That is deliberate: `player_projections` keeps its stat line nested
// under `StatLine`, and `war_room_briefs` buries decimals three levels down
// inside arrays (Leagues[].Starters[], TopBoomCandidates[], BustRisks[]).
// Enumerating literal paths would miss those; a name-driven walk does not.
//
// Every name below was checked against its own collection for collisions with a
// legitimately-string field. There are none — the identifiers in these documents
// (PlayerId, PlayerName, Position, GameScript, Basis) share no names with the
// numeric fields.
//
// NOT included, and deliberately so:
//   dynasty_valuations.TradeValue, career_simulations.CareerValueScore — these
//   are `double` in C#, already stored as BSON Double, and already sort correctly.
const TARGETS = {
  player_projections: [
    'ProjectedPoints', 'ProjectedPointsPpr', 'ProjectedPointsHalfPpr',
    'WeightedAvgPoints', 'MatchupAdjustmentFactor', 'SnapPctInput',
    'TargetShareInput', 'UsageTrendMultiplier', 'AvailabilityRate', 'RSquared',
    'RbVolumeMultiplier', 'WrTeVolumeMultiplier', 'SpreadInput',
    // nested: StatLine.*
    'PassingAttempts', 'Completions', 'PassingYards', 'PassingTds',
    'Interceptions', 'Carries', 'RushingYards', 'RushingTds', 'Targets',
    'Receptions', 'ReceivingYards', 'ReceivingTds', 'FumblesLost',
    'TwoPointConversions', 'SpecialTeamsTds'
  ],

  simulation_results: [
    'BaseProjection', 'StandardDeviation', 'Floor', 'Median', 'Ceiling', 'Mean',
    'BoomProbability', 'BustProbability', 'Spread'
  ],

  vorp_recommendations: [
    'ProjectedPoints', 'ReplacementLevel', 'Vorp', 'FloorPoints', 'CeilingPoints'
  ],

  emergence_alerts: ['Delta'],

  // Nested inside arrays — see the note above.
  war_room_briefs: [
    'Median', 'Floor', 'Ceiling', 'BoomProbability', 'BustProbability', 'Spread'
  ],

  PlayerGameLogs: [                    // PascalCase, unlike every other collection
    'PassingYards', 'SackYards', 'PassingAirYards', 'PassingYardsAfterCatch',
    'PassingEpa', 'Pacr', 'Dakota', 'RushingYards', 'RushingEpa',
    'ReceivingYards', 'ReceivingAirYards', 'ReceivingYardsAfterCatch',
    'ReceivingEpa', 'Racr', 'TargetShare', 'AirYardsShare', 'Wopr',
    'FantasyPoints', 'FantasyPointsPpr', 'PfrFantasyPoints', 'PfrVariance',
    'SnapPct'
  ],

  defensive_rankings: [
    'AvgFantasyPointsAllowed', 'AvgFantasyPointsAllowedL4W', 'SeasonPercentile',
    'L4WPercentile', 'DifficultyScore', 'SosAdjustedDifficultyScore'
  ],

  vegas_lines: ['HomeSpread', 'AwaySpread', 'OverUnder'],

  snap_counts: ['OffensePct'],

  // Already mapped to Decimal128 by explicit member serializers in
  // RegisterBsonClassMaps, so this should report zero conversions. Included
  // because any legacy string left here does not merely sort wrong — it throws
  // on read, since DecimalSerializer(Decimal128) will not accept a string.
  player_usage_metrics: [
    'TargetShare3Wk', 'TargetShare5Wk', 'TargetShareSeason',
    'AirYardsShare3Wk', 'AirYardsShare5Wk', 'AirYardsShareSeason',
    'Wopr3Wk', 'Wopr5Wk', 'WoprSeason',
    'CarryShare3Wk', 'CarryShare5Wk', 'CarryShareSeason',
    'SnapPct3Wk', 'SnapPct5Wk', 'SnapPctSeason',
    'ADot3Wk', 'ADot5Wk', 'ADotSeason',
    'Tprr3Wk', 'Tprr5Wk', 'TprrSeason'
  ]
};

// ── Walker ───────────────────────────────────────────────────────────────────

const NUMERIC = /^-?\d+(\.\d+)?([eE][-+]?\d+)?$/;

// Rewrites matching string fields in place. Returns [converted, skipped].
// `skipped` counts strings that matched a target name but did not parse — those
// are real data problems and are reported rather than guessed at.
function walk(node, names, stats) {
  if (node === null || typeof node !== 'object') return node;

  if (Array.isArray(node)) {
    for (let i = 0; i < node.length; i++) node[i] = walk(node[i], names, stats);
    return node;
  }

  for (const key of Object.keys(node)) {
    const val = node[key];

    if (names.has(key) && typeof val === 'string') {
      const trimmed = val.trim();
      if (NUMERIC.test(trimmed)) {
        node[key] = Decimal128.fromString(trimmed);
        stats.converted++;
      } else {
        stats.skipped++;
        if (stats.samples.length < 10)
          stats.samples.push({ field: key, value: val });
      }
    } else if (val !== null && typeof val === 'object') {
      node[key] = walk(val, names, stats);
    }
  }

  return node;
}

// ── Run ──────────────────────────────────────────────────────────────────────

const report = [];

for (const [collName, fields] of Object.entries(TARGETS)) {
  const coll = db.getCollection(collName);

  if (coll.countDocuments({}, { limit: 1 }) === 0) {
    report.push({ collection: collName, status: 'empty or missing' });
    continue;
  }

  const names = new Set(fields);
  const stats = { converted: 0, skipped: 0, samples: [] };
  let docsTouched = 0;
  let ops = [];

  const cursor = coll.find({});
  while (cursor.hasNext()) {
    const doc = cursor.next();
    const before = stats.converted;

    walk(doc, names, stats);

    if (stats.converted > before) {
      docsTouched++;
      const { _id, ...rest } = doc;
      ops.push({ replaceOne: { filter: { _id }, replacement: rest } });
    }

    if (!DRY_RUN && ops.length >= BATCH) {
      coll.bulkWrite(ops, { ordered: false });
      ops = [];
    }
  }

  if (!DRY_RUN && ops.length > 0) coll.bulkWrite(ops, { ordered: false });

  report.push({
    collection:    collName,
    docsTouched:   docsTouched,
    fieldsConverted: stats.converted,
    unparseable:   stats.skipped,
    unparseableSamples: stats.samples,
    status: DRY_RUN ? 'DRY RUN — nothing written' : 'written'
  });
}

print(JSON.stringify({ dryRun: DRY_RUN, report }, null, 2));

// ── After a real run ─────────────────────────────────────────────────────────
// Re-run _scratch/verify_0902.js. Section 0 should report "decimal" instead of
// "string", and the two lists in the sortProbe should finally agree.
//
// ONLY once that is true for an environment may the in-memory sorts in
// PlayerProjectionRepository, SimulationResultRepository, VorpRecommendationRepository
// and EmergenceAlertRepository be reverted — and they must not be reverted at all
// until every environment (dev, staging, prod) has been migrated, since the code
// is shared.
