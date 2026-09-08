# FAN-165 — diagnostic queries (MongoDB Compass, dev)

Connection: `ff-mongodb` on localhost:27017. Database: your dev DB (`fantasycombine` unless
Compass shows otherwise). Field names are PascalCase — no camelCase convention pack is
registered in `MongoDbContext`, so the C# property names are the stored names.

Collections used: `career_simulations`, `dynasty_valuations`.

---

## Query 1 — aging multipliers and season values for the named players

Collection: **`career_simulations`** → Aggregations tab → paste the whole array.

```javascript
[
  { $match: {
      Season: 2026,
      PlayerName: { $in: [
        "Saquon Barkley", "Christian McCaffrey", "Derrick Henry", "David Montgomery", "Chase Brown",
        "A.J. Brown", "DJ Moore", "Michael Pittman", "Jakobi Meyers",
        "Dak Prescott", "Jared Goff", "C.J. Stroud",
        "David Njoku", "Jared Wiley", "Brevin Jordan", "Cade Stover",
        "Elijah Higgins", "Daniel Bellinger", "Jeremy Ruckert"
      ] }
  } },
  { $sort: { ComputedAt: -1 } },
  { $group: { _id: "$SleeperPlayerId", doc: { $first: "$$ROOT" } } },
  { $replaceRoot: { newRoot: "$doc" } },
  { $project: {
      _id: 0,
      PlayerName: 1, Position: 1, CurrentAge: 1,
      CareerValueScore: 1, PeakYear: 1, YearsOfPrimeRemaining: 1,
      Ages:    "$YearProjections.AgeAtYear",
      Aging:   "$YearProjections.AgingMultiplier",
      Fppg:    "$YearProjections.MedianFppg",
      SeasonV: "$YearProjections.SeasonValue"
  } },
  { $sort: { Position: 1, CareerValueScore: -1 } }
]
```

**What I am looking for**

- `Aging` for Barkley / McCaffrey / Henry / Montgomery. If it reads `[0.1, 0.1, 0.1, 0.1, 0.1]`,
  hypothesis 3 is confirmed — the fallback RB curve floors at exactly age 29 and never recovers.
- `Fppg[0] / Aging[0]` recovers each player's shrunk `baseFppg`. For the backup TEs I expect
  either 3.5 (TE depth level — they were floored) or something near 9.0 (the TE prior carried
  them). Which one it is decides between hypothesis 1 and "the aging curve did it".
- **Any name that comes back missing is itself a finding** — it means `GetBaselineFppg` found no
  season-average sim row and the player is running on prior alone. Watch "A.J. Brown" and
  "DJ Moore" in particular; punctuation variants are a real risk here.

---

## Query 2 — where these players actually sit in their own position

Collection: **`dynasty_valuations`**. `$setWindowFields` needs MongoDB 5.0+; if it errors, use
Query 2b below instead.

```javascript
[
  { $match: { ModelValue: { $gt: 0 } } },
  { $setWindowFields: {
      partitionBy: "$Position",
      sortBy: { ModelValue: -1 },
      output: { PosRank: { $rank: {} } }
  } },
  { $match: { PlayerName: { $in: [
      "Saquon Barkley", "Christian McCaffrey", "Derrick Henry", "David Montgomery", "Chase Brown",
      "A.J. Brown", "DJ Moore", "Michael Pittman", "Jakobi Meyers",
      "Dak Prescott", "Jared Goff", "C.J. Stroud",
      "David Njoku", "Jared Wiley", "Brevin Jordan", "Cade Stover",
      "Elijah Higgins", "Daniel Bellinger", "Jeremy Ruckert"
  ] } } },
  { $project: {
      _id: 0, PlayerName: 1, Position: 1, PosRank: 1,
      ModelValue: 1, TradeValue: 1, Age: 1, YearsExperience: 1, CareerValueScore: 1
  } },
  { $sort: { Position: 1, PosRank: 1 } }
]
```

This is the direct test of the correction I made to the ticket: if A.J. Brown comes back at
`PosRank` ~46-70 rather than an overall rank near 404, the guardrail-cap reading is right and
the search moves to "why is he WR 50".

---

## Query 2b — fallback if `$setWindowFields` is unavailable

Run twice, once per position, and read the rank off the row number.

```javascript
[
  { $match: { Position: "WR", ModelValue: { $gt: 0 } } },
  { $sort: { ModelValue: -1 } },
  { $project: { _id: 0, PlayerName: 1, ModelValue: 1, TradeValue: 1, Age: 1, YearsExperience: 1 } },
  { $limit: 80 }
]
```

Swap `"WR"` for `"RB"` (limit 60) and `"QB"` (limit 40).

---

## Query 3 — the model's own top 15 tight ends

Collection: **`dynasty_valuations`**.

```javascript
[
  { $match: { Position: "TE", ModelValue: { $gt: 0 } } },
  { $sort: { ModelValue: -1 } },
  { $project: {
      _id: 0, PlayerName: 1, ModelValue: 1, TradeValue: 1,
      Age: 1, YearsExperience: 1, NflTeam: 1, CareerValueScore: 1
  } },
  { $limit: 15 }
]
```

The single most decisive result in this set. `GetTeGuardrailCap` is 70 for posRank <= 12 and 50
beyond it, and Wiley / Jordan / Stover all stamped **above** 50 — which is only possible if the
model has them inside its own top 12. This query says so outright, and names whoever else is up
there with them.
