# FAN-168 — TE diagnostic query (MongoDB Compass, dev)

Collection: **`career_simulations`** → Aggregations tab.

Twenty-nine tight ends: the seven the model has above the depth floor, the eleven-player
tie block at ranks 8–18, and the real starters it buries.

```javascript
[
  { $match: {
      Season: 2026,
      Position: "TE",
      PlayerName: { $in: [
        // above the floor
        "Trey McBride", "Tucker Kraft", "Brock Bowers", "Sam LaPorta",
        "George Kittle", "Dallas Goedert", "David Njoku",
        // the tie block, ranks 8-18
        "Jared Wiley", "Brevin Jordan", "Cade Stover", "Elijah Higgins",
        "Luke Musgrave", "Josh Whyle", "Tommy Tremble", "Julian Hill",
        "Dallen Bentley", "Davis Allen", "Daniel Bellinger", "Jeremy Ruckert",
        // buried starters
        "Travis Kelce", "Mark Andrews", "T.J. Hockenson", "Evan Engram",
        "Dalton Schultz", "Hunter Henry", "Dalton Kincaid", "Kyle Pitts",
        "Isaiah Likely", "Brenton Strange"
      ] }
  } },
  { $sort: { ComputedAt: -1 } },
  { $group: { _id: "$SleeperPlayerId", doc: { $first: "$$ROOT" } } },
  { $replaceRoot: { newRoot: "$doc" } },
  { $addFields: {
      // baseFppg recovered: the shrinkage output, before aging.
      BaseFppg: { $round: [ { $divide: [
          { $arrayElemAt: ["$YearProjections.MedianFppg", 0] },
          { $arrayElemAt: ["$YearProjections.AgingMultiplier", 0] }
      ] }, 2 ] },
      MeanAging: { $round: [ { $avg: "$YearProjections.AgingMultiplier" }, 3 ] }
  } },
  { $project: {
      _id: 0,
      PlayerName: 1, CurrentAge: 1, CareerValueScore: 1, YearsOfPrimeRemaining: 1,
      BaseFppg: 1, MeanAging: 1,
      Ages:  "$YearProjections.AgeAtYear",
      Aging: "$YearProjections.AgingMultiplier",
      SeasonV: "$YearProjections.SeasonValue"
  } },
  { $sort: { CareerValueScore: -1 } }
]
```

## What each column decides

**`BaseFppg`** is the whole ticket. `GetDepthLevelFppg("TE")` is **3.5**. Every player in the
tie block should come back at exactly 3.5 — that is floor 1, and it is why eleven players
land within 2.1 points of each other with no ordering between them.

**`CurrentAge` + `MeanAging`** is floor 2. The TE fallback curve peaks at 27 and clamps at 0.1
once a player is 8 years past it, so anyone at 35+ reads 0.1 across all five years. A real
starter whose `BaseFppg` is well above 3.5 can still finish below the block once `MeanAging`
drops far enough — that is the sandwich.

**`Ages` next to `Aging`** catches a wrong `Player.Age`, which is a live risk: FAN-157 recorded
that `CurrentYear` was a hardcoded 2026 and that ages come from `Player.Age` (age *today*), so
every age-at-game shifts by one on 1 January.

## The one row I most want to see

**T.J. Hockenson.** He came back at RV 26.2, roughly half the tie block's ~52, and I cannot
account for it. `ApplyShrinkage` cannot return below 3.5, and if he is the age I assumed his
`MeanAging` should be ~0.85 — which lands near 45, not 26.

Correction worth stating plainly: **I asserted he is 28. The calibration output carries no age
column, so that was my own recall, not your data.** If `CurrentAge` comes back at 33+, there is
no anomaly and the two floors explain everything. If it comes back at 28 or 29, there is a third
mechanism neither FAN-168 nor FAN-167 describes, and both tickets are built on an incomplete
model — which is worth knowing before either of them changes a constant.

Mark Andrews and Evan Engram are the same test at less extreme ages.
