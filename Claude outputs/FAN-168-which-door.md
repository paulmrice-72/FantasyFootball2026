# FAN-168 — which door did they go through?

Collection: **`simulation_results`** → Aggregations tab. Dev.

Both paths in `ApplyShrinkage` end at 3.5, and the fix is different depending on which one
these players took:

* **Door A — the gate.** They have a real season-average row, the blend was computed, and it
  came in under `StarterThreshold["TE"] = 8.5`, so the gate floored them. The bug is the gate,
  and the fix is in `CareerSimulationService`.
* **Door B — no data.** `GetBaselineFppg` found no season-average row at all, so `rawFppg` is 0,
  `hasStarterEvidence` is false, and with `YearsExperience >= 1` the method returns `depthLevel`
  before the gate ever runs. The bug is the seeding pipeline, and no amount of work on the
  shrinkage math touches it.

I can't tell them apart from `career_simulations` — both write 3.5. This query can.

```javascript
[
  { $match: {
      Week: 0,
      Position: "TE",
      PlayerName: { $in: [
        "Mark Andrews", "Kyle Pitts", "Dalton Kincaid", "Isaiah Likely",
        "T.J. Hockenson", "Evan Engram", "Dalton Schultz", "Hunter Henry",
        "Brenton Strange", "Jared Wiley", "Brevin Jordan", "Cade Stover",
        "Jeremy Ruckert", "Daniel Bellinger",
        "Travis Kelce", "David Njoku", "Dallas Goedert", "Trey McBride"
      ] }
  } },
  { $project: {
      _id: 0, PlayerName: 1, Season: 1, Median: 1, Mean: 1,
      BaseProjection: 1, PlayerRole: 1, ScoringFormat: 1, SleeperPlayerId: 1
  } },
  { $sort: { PlayerName: 1, Season: -1 } }
]
```

## Reading it

The last four are the control group — they cleared the gate, so they must come back with rows.
If they don't, the query is wrong before anything else is concluded.

For the eight floored starters:

* **Rows present, `Median` roughly 6–8** → Door A. The gate is doing it, and the numbers also
  tell us where the cliff actually bites. `blended = 0.625 × Median + 3.375`, so anything with a
  `Median` under 8.2 fails at 5+ years of experience.
* **No rows** → Door B, and the real ticket is that `seed-season-averages` is not covering the
  tight end position. Worth knowing before touching a constant: the 2026-08-23 run logged
  "seeded 606, skipped 0, unmatched 4" across all four positions, and nobody has checked the
  per-position split.
* **Mixed** → both doors are open, and the gate fix alone leaves half the position flat.

`PlayerRole` matters too. `IsSeasonAverageRow` keeps a row only when `PlayerRole` is empty or
exactly `"SeasonAverage"`; anything else is treated as a current-season projection and
discarded, which is the Fagnano guard from 09-06. A row labelled something else would look
present in this query and still be invisible to the baseline lookup.
