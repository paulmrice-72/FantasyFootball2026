// ─────────────────────────────────────────────────────────────────────────────
// Replaces query 2a. Run this ONE thing in the mongosh tab, export the ONE
// result to _scratch/2a_v2.json.
//
// Everything comes back in a single object, so there's no way for three separate
// exports to get crossed — which is what went wrong the first time.
//
// It also reports which database and host it ran against, which settles whether
// the earlier run was pointed at dev or the prod tunnel.
// ─────────────────────────────────────────────────────────────────────────────

(function () {
  // Collections the rookie prior and the projection engine care about.
  const interesting = [
    'pff_draft_grades',
    'fantasyPros_rookie_rankings',
    'combine_results',
    'depth_charts',
    'player_projections',
    'simulation_results',
    'player_game_logs',
    'player_usage_metrics',
    'redraftAdpCache'
  ];

  const existing = db.getCollectionNames().sort();

  const out = {
    // ── Which database am I actually looking at ───────────────────────────
    database: db.getName(),
    host: String(db.getMongo()),
    generatedAt: new Date().toISOString(),

    // ── Everything that exists, with row counts ───────────────────────────
    // This also tells me the real name of the roster collection, which I
    // guessed at in query 2c.
    allCollections: existing.map(function (n) {
      return { name: n, docs: db.getCollection(n).estimatedDocumentCount() };
    }),

    // ── Per-season breakdown, only for collections that exist ─────────────
    bySeason: {}
  };

  interesting.forEach(function (name) {
    if (existing.indexOf(name) === -1) {
      out.bySeason[name] = 'COLLECTION DOES NOT EXIST';
      return;
    }

    try {
      out.bySeason[name] = db.getCollection(name).aggregate([
        { $group: {
            _id: '$Season',
            rows: { $sum: 1 },
            withSleeperId: {
              $sum: { $cond: [{ $gt: ['$SleeperPlayerId', ''] }, 1, 0] }
            }
        }},
        { $sort: { _id: -1 } },
        { $limit: 8 }
      ]).toArray();
    } catch (e) {
      out.bySeason[name] = 'ERROR: ' + e.message;
    }
  });

  return out;
})()
