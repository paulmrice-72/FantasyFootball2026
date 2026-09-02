// ─────────────────────────────────────────────────────────────────────────────
// FAN-129 — convert string-stored decimals to Decimal128.  v2, for STAGING/PROD.
//
// Differs from v1 in two ways that matter once something other than you is
// writing to the database:
//
//   1. Targeted $set of individual dotted paths instead of full-document
//      replaceOne. v1 read a whole document and wrote the whole thing back, so
//      any write by a Hangfire job landing between the read and the write would
//      be silently reverted. Harmless on dev where nothing else runs. On prod,
//      in-season, with the refresh jobs live, that is data loss. This version
//      only ever touches the exact fields it converts.
//
//   2. A connection guard. Dev and prod use the SAME database name
//      (`fantasycombine`), so `db.getName()` cannot tell you where you are —
//      only the connection can. Set EXPECT_HOST below and the script refuses to
//      run anywhere else.
//
// Run ORDER, per environment: deploy the TolerantDecimalSerializer FIRST, then
// migrate. Old code reads Decimal128 without complaint but keeps WRITING
// strings, so migrating first just means re-polluting the collection and having
// to run this again.
// ─────────────────────────────────────────────────────────────────────────────

const DRY_RUN     = true;      // ← flip to false to actually write
const BATCH       = 500;

// Substring that must appear in db.serverStatus().host — this is the container
// hostname, so it is 'ff-mongodb-staging' for staging and 'ff-mongodb' for prod.
// Leave '' only when you genuinely do not care which box you are on.
const EXPECT_HOST = 'ff-mongodb-staging';

// ── Guard ────────────────────────────────────────────────────────────────────

const actualHost = db.serverStatus().host;
const actualDb   = db.getName();

print(`connection host : ${actualHost}`);
print(`database        : ${actualDb}`);
print(`mode            : ${DRY_RUN ? 'DRY RUN' : '*** WRITING ***'}`);
print('');

if (EXPECT_HOST && !actualHost.includes(EXPECT_HOST)) {
  throw new Error(
    `Refusing to run: connected to '${actualHost}', expected a host containing ` +
    `'${EXPECT_HOST}'. Fix the connection or change EXPECT_HOST — do not delete this check.`);
}

// ── The field inventory ──────────────────────────────────────────────────────
// Identical to v1. Derived from the `public decimal` properties on the FF.Domain
// document types, mapped to the collection each repository opens. Matched by
// field NAME and walked recursively, because player_projections nests its stat
// line under StatLine and war_room_briefs buries decimals inside
// Leagues[].Starters[], Leagues[].KeyDecisions[], TopBoomCandidates[], BustRisks[].

const TARGETS = {
  player_projections: [
    'ProjectedPoints', 'ProjectedPointsPpr', 'ProjectedPointsHalfPpr',
    'WeightedAvgPoints', 'MatchupAdjustmentFactor', 'SnapPctInput',
    'TargetShareInput', 'UsageTrendMultiplier', 'AvailabilityRate', 'RSquared',
    'RbVolumeMultiplier', 'WrTeVolumeMultiplier', 'SpreadInput',
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
  war_room_briefs: [
    'Median', 'Floor', 'Ceiling', 'BoomProbability', 'BustProbability', 'Spread'
  ],
  PlayerGameLogs: [
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

// ── Walker: collects dotted paths, mutates nothing ───────────────────────────

const NUMERIC = /^-?\d+(\.\d+)?([eE][-+]?\d+)?$/;

function collect(node, names, prefix, out, stats) {
  if (node === null || typeof node !== 'object') return;

  if (Array.isArray(node)) {
    for (let i = 0; i < node.length; i++)
      collect(node[i], names, `${prefix}${i}.`, out, stats);
    return;
  }

  for (const key of Object.keys(node)) {
    const val  = node[key];
    const path = `${prefix}${key}`;

    if (names.has(key) && typeof val === 'string') {
      const trimmed = val.trim();
      if (NUMERIC.test(trimmed)) {
        out[path] = Decimal128.fromString(trimmed);
      } else {
        stats.skipped++;
        if (stats.samples.length < 10) stats.samples.push({ path, value: val });
      }
    } else if (val !== null && typeof val === 'object') {
      collect(val, names, `${path}.`, out, stats);
    }
  }
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
  const stats = { skipped: 0, samples: [] };
  let docsTouched = 0, fieldsConverted = 0, ops = [];

  const cursor = coll.find({});
  while (cursor.hasNext()) {
    const doc = cursor.next();
    const sets = {};

    collect(doc, names, '', sets, stats);

    const n = Object.keys(sets).length;
    if (n > 0) {
      docsTouched++;
      fieldsConverted += n;
      // $set only the converted paths. Everything else in the document is left
      // untouched, so a concurrent job write cannot be reverted by this run.
      ops.push({ updateOne: { filter: { _id: doc._id }, update: { $set: sets } } });
    }

    if (!DRY_RUN && ops.length >= BATCH) {
      coll.bulkWrite(ops, { ordered: false });
      ops = [];
    }
  }

  if (!DRY_RUN && ops.length > 0) coll.bulkWrite(ops, { ordered: false });

  report.push({
    collection: collName,
    docsTouched,
    fieldsConverted,
    unparseable: stats.skipped,
    unparseableSamples: stats.samples,
    status: DRY_RUN ? 'DRY RUN — nothing written' : 'written'
  });
}

print(JSON.stringify({
  host: actualHost, database: actualDb, dryRun: DRY_RUN, report
}, null, 2));
