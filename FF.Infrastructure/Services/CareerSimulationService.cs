using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using MathNet.Numerics.Distributions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using FF.Application.Services;

namespace FF.Infrastructure.Services;

public class CareerSimulationService(
    IPlayerRepository playerRepository,
    IAgingCurveRepository agingCurveRepository,
    ISimulationResultRepository simulationResultRepository,
    ILogger<CareerSimulationService> logger) : ICareerSimulationService
{
    private const int Iterations = 1000;
    private const int ProjectYears = 5;
    private const int CurrentSeason = 2026;

    // ── Empirical Bayes shrinkage ─────────────────────────────────────────
    // credibility = min(YearsExp, 5) / (min(YearsExp, 5) + K)
    // blended = credibility × raw + (1 - credibility) × prior
    // K=3: rookie → 0% credibility (full prior), 5yr vet → 62.5% (cap)
    // Admin-configurable in a future sprint (ADMIN-WEIGHT-001).
    private const double ShrinkageK = 3.0;

    // ── FAN-95: Elite-tier post-peak aging dampening ────────────────────────
    // The aging curve (AgingCurveService) is a single population-average
    // polynomial fit across ALL players at a position — it can't distinguish
    // a proven, durable elite talent from a replacement-level one and applies
    // the same post-peak decline to both. Verified 2026-08-25: this was
    // pulling Josh Allen's CareerValueScore below Brock Purdy's/Bo Nix's
    // despite Allen's shrunk baseline FPPG being clearly higher every
    // projected year — and contradicting real Superflex market consensus
    // (FantasyPros), which keeps proven elite QBs well ahead of unproven
    // younger ones despite the age gap.
    //
    // Fix: rank players within their position by shrunk baseline FPPG (a
    // real, current, evidence-based signal — not a player-specific override)
    // and soften the post-peak decay proportionally for the top tier. Applied
    // in SimulatePlayer via eliteTierFactor. Tunable via the calibration
    // harness (FAN-95) like ShrinkageK and NormExponent elsewhere.
    private const double EliteDecayDampening = 0.6;

    // Position priors per scoring math reference doc (FAN-61).
    // These represent average STARTER FPPG by position in half-PPR.
    //
    // QB prior = 18.5: This is the median starter QB FPPG. The scoring doc
    // explicitly designed P1 shrinkage around this value. At 14.0 (previous),
    // shrinkage was pulling proven QBs DOWN too aggressively, compressing
    // Allen/Hurts/Jackson toward the same band as Purdy/Goff — then requiring
    // hand-tuned cap tables downstream in DfvCalculationService to fix the
    // ordering. With 18.5, the shrinkage formula naturally differentiates:
    //   - Allen (5+ yrs, 28 FPPG raw): blended = 62.5% × 28 + 37.5% × 18.5 = 24.44
    //   - Milton (1 yr, 19 FPPG raw):  blended = 25% × 19 + 75% × 18.5 = 18.63
    //   - Rookie (0 yrs):              blended = 100% × 18.5 = 18.5 (or depth gate)
    //
    // The journeyman QB cap (21.0 at age 28+ / exp 8+) still catches
    // Mayfield/Darnold/Goff without the prior change affecting them differently.
    //
    // TE prior = 9.0 (FAN-52): raised from 7.5 — real TE1 production average.
    private static readonly Dictionary<string, double> PositionPriors = new()
    {
        ["QB"] = 18.5, // FAN-61: median starter, per scoring math reference doc
        ["RB"] = 9.5,  // accounts for committee backs
        ["WR"] = 9.0,  // slot + role players drag median down
        ["TE"] = 9.0,  // FAN-52: raised from 7.5 — real TE1 average
    };

    private static readonly Dictionary<string, double> BaseInjuryRisk = new()
    {
        ["QB"] = 0.12,
        ["RB"] = 0.22,
        ["WR"] = 0.15,
        ["TE"] = 0.14
    };

    private static readonly Dictionary<string, double> AgeInjuryIncrement = new()
    {
        ["QB"] = 0.015,
        ["RB"] = 0.030,
        ["WR"] = 0.018,
        ["TE"] = 0.020
    };

    private static readonly Dictionary<string, int> PeakAges = new()
    {
        ["QB"] = 29,
        ["RB"] = 24,
        ["WR"] = 26,
        ["TE"] = 27
    };

    // FAN-52: TE threshold raised from 6.0 → 8.5.
    // 6.0 allowed any TE whose blended FPPG cleared a backup RB's average
    // to be treated as a starter. At 8.5, only genuine TE1-caliber players
    // (confirmed targets, inline starters) pass; TE2s fall to depth level (3.5).
    private static readonly Dictionary<string, double> StarterThreshold = new()
    {
        ["QB"] = 16.0,
        ["RB"] = 7.0,
        ["WR"] = 7.5,
        ["TE"] = 8.5, // FAN-52: raised from 6.0
    };

    private static readonly Dictionary<string, double> PostPeakWindow = new()
    {
        ["QB"] = 8.0,
        ["RB"] = 5.0,
        ["WR"] = 9.0,
        ["TE"] = 8.0,
    };

    public async Task<List<CareerSimulationDocument>> SimulateAllPlayersAsync(
        int season, CancellationToken ct = default)
    {
        var results = new List<CareerSimulationDocument>();
        var positions = new[] { Position.QB, Position.RB, Position.WR, Position.TE };

        // ── Bulk-load aging curves ───────────────────────────────────────
        var curves = new Dictionary<string, AgingCurveDocument?>();
        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
            curves[pos] = await agingCurveRepository.GetByPositionAsync(pos, ct);

        // ── Bulk-load ALL season-average sim results in ONE query ────────
        var allSimResults = await simulationResultRepository.GetAllSeasonAveragesAsync(ct);
        logger.LogInformation(
            "Bulk-loaded {Count} season-average sim results for baseline lookup",
            allSimResults.Count);

        // Multi-season merge — average 2024+2025 where both exist.
        // Prevents one outlier season (Darnold 2024: 18.1) from seeding
        // an inflated 5-year projection. Uses best single season only as fallback.
        var simByPlayerId = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId) && r.Median > 0
                        && IsSeasonAverageRow(r))
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var seasons = g.OrderByDescending(r => r.Season).ToList();
                    if (seasons.Count == 1) return seasons[0];
                    // Average the two most recent seasons
                    var recent = seasons[0];
                    var prior = seasons[1];
                    return new SimulationResultDocument
                    {
                        SleeperPlayerId = recent.SleeperPlayerId,
                        PlayerName = recent.PlayerName,
                        Position = recent.Position,
                        NflTeam = recent.NflTeam,
                        Season = recent.Season,
                        Week = 0,
                        Median = Math.Round((recent.Median + prior.Median) / 2, 2),
                        Floor = Math.Round((recent.Floor + prior.Floor) / 2, 2),
                        Ceiling = Math.Round((recent.Ceiling + prior.Ceiling) / 2, 2),
                        Mean = Math.Round((recent.Mean + prior.Mean) / 2, 2),
                        BaseProjection = Math.Round((recent.BaseProjection + prior.BaseProjection) / 2, 2),
                        StandardDeviation = recent.StandardDeviation,
                        ScoringFormat = recent.ScoringFormat,
                        CalculatedAt = DateTime.UtcNow,
                        PlayerRole = "SeasonAverage"
                    };
                });

        var simByNamePos = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.PlayerName) && r.Median > 0
                        && IsSeasonAverageRow(r))
            .GroupBy(r => $"{r.PlayerName}|{r.Position}")
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var seasons = g.OrderByDescending(r => r.Season).ToList();
                    if (seasons.Count == 1) return seasons[0];
                    var recent = seasons[0];
                    var prior = seasons[1];
                    return new SimulationResultDocument
                    {
                        SleeperPlayerId = recent.SleeperPlayerId,
                        PlayerName = recent.PlayerName,
                        Position = recent.Position,
                        Season = recent.Season,
                        Week = 0,
                        Median = Math.Round((recent.Median + prior.Median) / 2, 2),
                        Floor = Math.Round((recent.Floor + prior.Floor) / 2, 2),
                        Ceiling = Math.Round((recent.Ceiling + prior.Ceiling) / 2, 2),
                        Mean = Math.Round((recent.Mean + prior.Mean) / 2, 2),
                        BaseProjection = Math.Round((recent.BaseProjection + prior.BaseProjection) / 2, 2),
                        StandardDeviation = recent.StandardDeviation,
                        ScoringFormat = recent.ScoringFormat,
                        CalculatedAt = DateTime.UtcNow,
                        PlayerRole = "SeasonAverage"
                    };
                });

        // ── Simulate each player ─────────────────────────────────────────
        foreach (var position in positions)
        {
            var players = (await playerRepository.GetByPositionAsync(position, ct))
                .GroupBy(p => p.SleeperPlayerId)
                .Select(g => g.First())
                .ToList();
            var posStr = position.ToString();

            // FAN-95: rank this position's players by shrunk baseline FPPG so
            // SimulatePlayer can dampen post-peak decay for the proven top tier.
            var eliteTierByPlayerId = BuildEliteTierMap(players, posStr, simByPlayerId, simByNamePos);

            foreach (var player in players)
            {
                if (player.SleeperPlayerId is null) continue;

                // 2026-09-07: Sleeper's player table carries placeholder rows for
                // retired and void entries. SeedSeasonAverageSimsCommandHandler has
                // always filtered them; this pipeline never did, so a row literally
                // named "Duplicate Player" was simulated, valued, and surfaced on
                // the dynasty board at TradeValue 81.8 — around 20th overall.
                if (PlayerNameNormalizer.IsPlaceholder(player.FullName)) continue;

                if (!player.Age.HasValue && player.YearsExperience != 0) continue;
                if (player.Age.HasValue && player.Age.Value < 18) continue;

                try
                {
                    var eliteTier = eliteTierByPlayerId.GetValueOrDefault(player.SleeperPlayerId, 0.0);
                    var sim = SimulatePlayer(
                        player, posStr, curves[posStr], season,
                        simByPlayerId, simByNamePos, eliteTier);
                    results.Add(sim);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Career sim failed for {Player}", player.FullName);
                }
            }
        }

        logger.LogInformation("Career simulations complete — {Count} players", results.Count);
        return results;
    }

    public async Task<CareerSimulationDocument> SimulatePlayerCareerAsync(
        string sleeperPlayerId, CancellationToken ct = default)
    {
        var player = await playerRepository.GetBySleeperIdAsync(sleeperPlayerId, ct)
                     ?? throw new InvalidOperationException($"Player not found: {sleeperPlayerId}");

        var posStr = player.Position.ToString();
        var curve = await agingCurveRepository.GetByPositionAsync(posStr, ct);

        var allSimResults = await simulationResultRepository.GetAllSeasonAveragesAsync(ct);
        // 2026-09-07: this single-player path did not filter on Week at all, so a
        // one-off recompute could seed a career from a single WEEK's simulation
        // while the bulk path used season averages. Same rows, same filter, both
        // paths now.
        var simByPlayerId = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId) && r.Median > 0
                        && IsSeasonAverageRow(r))
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Season).First());

        var simByNamePos = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.PlayerName) && r.Median > 0
                        && IsSeasonAverageRow(r))
            .GroupBy(r => $"{r.PlayerName}|{r.Position}")
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Season).First());

        // FAN-95: same elite-tier ranking the bulk path uses, so a single-player
        // recompute (e.g. an admin "recalculate this player" action) agrees with
        // SimulateAllPlayersAsync instead of silently using eliteTierFactor = 0.
        var positionPlayers = (await playerRepository.GetByPositionAsync(player.Position, ct))
            .GroupBy(p => p.SleeperPlayerId)
            .Select(g => g.First())
            .ToList();
        var eliteTierByPlayerId = BuildEliteTierMap(positionPlayers, posStr, simByPlayerId, simByNamePos);
        var eliteTier = eliteTierByPlayerId.GetValueOrDefault(sleeperPlayerId, 0.0);

        return SimulatePlayer(player, posStr, curve, CurrentSeason, simByPlayerId, simByNamePos, eliteTier);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private CareerSimulationDocument SimulatePlayer(
        FF.Domain.Entities.Player player,
        string position,
        AgingCurveDocument? curve,
        int season,
        Dictionary<string, SimulationResultDocument> simByPlayerId,
        Dictionary<string, SimulationResultDocument> simByNamePos,
        double eliteTierFactor = 0.0)
    {
        var currentAge = player.Age ?? (player.YearsExperience == 0 ? 21 : 22);
        var peakAgeForPosition = curve?.PeakAge ?? PeakAges.GetValueOrDefault(position, 26);

        var rawBaseline = GetBaselineFppg(
            player.SleeperPlayerId!, player.FullName, position,
            simByPlayerId, simByNamePos);

        var baseFppg = ApplyShrinkage(position, rawBaseline, player);

        if (baseFppg <= 0)
            baseFppg = GetDepthLevelFppg(position);

        var yearProjections = new List<CareerYearProjection>();

        // 2026-09-07: was `new Random()` — unseeded, so every run drew a
        // different career for the same player on the same data. Two identical
        // players came out 624.1 and 623.9 in a unit test, which is how this was
        // noticed, but the consequence is larger than a flaky assertion:
        // CareerValueScore fed TradeValue fed the dynasty board, so the board
        // reshuffled on every recalculation and every FAN-95 calibration delta
        // measured against FantasyPros carried dice in it. Some of those small
        // run-to-run movements were the model and some were noise, and after the
        // fact there was no way to tell which.
        //
        // Seeded per player and season: a given player's career is now
        // reproducible, while the population still varies player to player.
        var rng = new Random(StableSeed(player.SleeperPlayerId!, season));

        for (int yearOffset = 0; yearOffset < ProjectYears; yearOffset++)
        {
            var projYear = season + yearOffset;
            var ageAtYear = currentAge + yearOffset;
            var aging = GetAgingMultiplier(curve, position, ageAtYear);

            // FAN-95: dampen post-peak decay for players ranked in the top
            // tier of their position by shrunk baseline FPPG (see
            // BuildEliteTierMap). Pre-peak ascent is untouched — this only
            // softens the decline for players who've already proven elite,
            // durable production, not a blanket QB boost.
            if (eliteTierFactor > 0 && ageAtYear > peakAgeForPosition)
            {
                aging += eliteTierFactor * EliteDecayDampening * (1.0 - aging);
            }

            var injury = GetInjuryRisk(position, ageAtYear);

            var yearSamples = new double[Iterations];
            var stdDev = baseFppg * aging * GetPositionVariance(position);

            for (int i = 0; i < Iterations; i++)
            {
                var projected = Normal.Sample(rng, baseFppg * aging, stdDev);
                var injuryRoll = rng.NextDouble();
                var gamesPlayed = injuryRoll < injury
                    ? 17.0 * (1.0 - injury * 0.6)
                    : 17.0;
                yearSamples[i] = Math.Max(0, projected * (gamesPlayed / 17.0));
            }

            Array.Sort(yearSamples);

            var median = yearSamples[Iterations / 2];
            var floor = yearSamples[(int)(Iterations * 0.10)];
            var ceiling = yearSamples[(int)(Iterations * 0.90)];

            yearProjections.Add(new CareerYearProjection
            {
                Year = projYear,
                AgeAtYear = ageAtYear,
                AgingMultiplier = aging,
                MedianFppg = Math.Round(median, 2),
                FloorFppg = Math.Round(floor, 2),
                CeilingFppg = Math.Round(ceiling, 2),
                InjuryRisk = Math.Round(injury, 3),
                ExpectedGamesPlayed = Math.Round(17.0 * (1.0 - injury), 1),
                SeasonValue = Math.Round(median * 17.0 * (1.0 - injury), 1),
                Phase = ClassifyPhase(position, ageAtYear)
            });
        }

        var careerValue = yearProjections
            .Select((y, i) => y.SeasonValue / Math.Pow(1.15, i))
            .Sum();

        var peakYear = yearProjections.MaxBy(y => y.SeasonValue)!;
        var primeYears = yearProjections.Count(y => y.AgingMultiplier >= 0.70);

        return new CareerSimulationDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            SleeperPlayerId = player.SleeperPlayerId!,
            PlayerName = player.FullName,
            Position = position,
            CurrentAge = currentAge,
            Season = season,
            CareerPhase = ClassifyPhase(position, currentAge),
            YearProjections = yearProjections,
            CareerValueScore = Math.Round(careerValue, 1),
            PeakYearValue = peakYear.SeasonValue,
            PeakYear = peakYear.Year,
            YearsOfPrimeRemaining = primeYears,
            ComputedAt = DateTime.UtcNow,
            Iterations = Iterations
        };
    }

    /// <summary>
    /// FAN-95: Ranks a position's players by shrunk baseline FPPG and buckets
    /// them into a tiered dampening factor for SimulatePlayer's post-peak
    /// aging decay. Rank-based (like P2 normalization elsewhere in the
    /// pipeline) rather than a fixed raw-FPPG threshold, so it stays stable
    /// as the player pool and scoring environment shift year to year — and
    /// it's derived from real per-season data, not any specific player name.
    ///
    /// Tiers: top 3 at the position → full dampening (1.0), 4-8 → half (0.5),
    /// everyone else → none (0.0). Mirrors the tier-boundary style already
    /// used for the QB/TE guardrail caps in DfvCalculationService.
    /// </summary>
    private Dictionary<string, double> BuildEliteTierMap(
        List<FF.Domain.Entities.Player> players,
        string position,
        Dictionary<string, SimulationResultDocument> simByPlayerId,
        Dictionary<string, SimulationResultDocument> simByNamePos)
    {
        var baselines = new List<(string Id, double BaseFppg)>();
        foreach (var player in players)
        {
            if (player.SleeperPlayerId is null) continue;
            // Same exclusion as the simulate loop — a placeholder must not occupy
            // a slot in the elite tier ranking either.
            if (PlayerNameNormalizer.IsPlaceholder(player.FullName)) continue;

            var raw = GetBaselineFppg(
                player.SleeperPlayerId, player.FullName, position,
                simByPlayerId, simByNamePos);
            var blended = ApplyShrinkage(position, raw, player);
            baselines.Add((player.SleeperPlayerId, blended));
        }

        var ranked = baselines.OrderByDescending(b => b.BaseFppg).ToList();
        var tierMap = new Dictionary<string, double>();
        for (int i = 0; i < ranked.Count; i++)
        {
            var rank = i + 1;
            tierMap[ranked[i].Id] = rank switch
            {
                <= 3 => 1.0,
                <= 8 => 0.5,
                _ => 0.0
            };
        }

        return tierMap;
    }

    /// <summary>
    /// Pure in-memory baseline lookup — no DB calls. Volatility discount
    /// removed; shrinkage handles low-evidence players naturally.
    /// </summary>
    private double GetBaselineFppg(
        string sleeperPlayerId,
        string playerName,
        string position,
        Dictionary<string, SimulationResultDocument> simByPlayerId,
        Dictionary<string, SimulationResultDocument> simByNamePos)
    {
        SimulationResultDocument? sim = null;

        if (simByPlayerId.TryGetValue(sleeperPlayerId, out var byId) && byId.Median > 0)
            sim = byId;
        else
        {
            var key = $"{playerName}|{position}";
            if (simByNamePos.TryGetValue(key, out var byName) && byName.Median > 0)
            {
                logger.LogDebug(
                    "Used name fallback for {Player} — SleeperPlayerId {Id} had no sim result",
                    playerName, sleeperPlayerId);
                sim = byName;
            }
        }

        if (sim is null) return 0;

        return (double)sim.Median;
    }

    /// <summary>
    /// Empirical Bayes shrinkage — blends raw FPPG toward position prior
    /// weighted by evidence (years of experience).
    ///
    /// credibility = min(YearsExp, 5) / (min(YearsExp, 5) + K)
    /// blended = credibility × raw + (1 - credibility) × prior
    ///
    /// K=3: 0 yrs → 0% (full prior) 1 yr → 25% 3 yrs → 50% 5+ yrs → 62.5%
    ///
    /// With QB prior at 18.5 (FAN-61), shrinkage naturally produces:
    ///   Allen (5yr, 28 raw) → 24.44 — elite, mostly trusted
    ///   Milton (1yr, 19 raw) → 18.63 — pulled toward starter average
    ///   Purdy (3yr, 20 raw) → 19.25 — moderate credibility
    ///   Rookie (0yr, no data) → 18.5 (prior) or depth gate (no draft pedigree)
    ///   Ehlinger (4yr, no data) → 6.0 (depth) — career backup, not unknown
    ///
    /// Journeyman cap: age 28+, exp 8+ QBs capped at 21.0 FPPG blended.
    /// Catches Mayfield/Darnold/Goff without affecting Allen/Burrow/Hurts.
    ///
    /// Age regression multipliers apply in SimulatePlayer AFTER this returns.
    /// </summary>
    private static double ApplyShrinkage(
        string position,
        double rawFppg,
        FF.Domain.Entities.Player player)
    {
        var prior = PositionPriors.GetValueOrDefault(position, 9.0);
        var depthLevel = GetDepthLevelFppg(position);
        var clampedExp = Math.Min(player.YearsExperience ?? 0, 5);
        var credibility = clampedExp / (clampedExp + ShrinkageK);

        // ── 2026-09-07: two defects, one root ─────────────────────────────
        //
        // (1) Every gate below used to test `rawFppg <= 0` — literally no data
        //     at all. The moment a player had ANY measured rate, however tiny,
        //     all of them were bypassed and he fell through to the standard
        //     blend, where a rookie's credibility of zero hands him 100% of the
        //     STARTER prior.
        //
        //     Measured case: Joe Fagnano, Baltimore's QB3, carries a 2026 sim
        //     median of 0.17 FPPG. That 0.17 was enough to skip the rookie gate,
        //     and he emerged at 18.5 — the median starting quarterback — for a
        //     five-year career. CareerValueScore 971, TradeValue 94.5, first
        //     overall on the dynasty board, ahead of Mahomes.
        //
        //     Having a little evidence was strictly worse than having none. The
        //     gate now asks whether a player has evidence of being a STARTER,
        //     which is the question the starter prior is conditioned on.
        //
        // (2) Draft capital was two ad-hoc gates that disagreed with each other:
        //     a QB earned the full starter prior only as a 1st-round pick, while
        //     every other position earned it with ANY draft round on file. That
        //     is how Max Bredeson, a late-round tight end, was modelled as a
        //     proven TE1 at 9.0 FPPG. Both collapse into one curve below,
        //     applied identically at every position.
        //
        // Note what is deliberately NOT touched: for anyone with real experience
        // the prior is unchanged, so the FAN-95 calibration on the veteran
        // population — the part that was tuned against FantasyPros consensus —
        // sees no movement from this.
        var hasStarterEvidence = rawFppg >= depthLevel;

        // A rookie's credibility is zero, which means the prior IS his
        // projection. Scale that prior by draft capital so an undrafted rookie
        // cannot inherit the median starter's season simply by existing in the
        // player table.
        if ((player.YearsExperience ?? 0) == 0)
        {
            prior = depthLevel + (DraftPedigreeWeight(player.DraftRound) * (prior - depthLevel));
        }

        if (!hasStarterEvidence)
        {
            // Experience with nothing to show for it is itself evidence: a
            // career backup, not an unknown quantity.
            if ((player.YearsExperience ?? 0) >= 1)
                return depthLevel;

            // Zero experience and no starter-level production — draft capital
            // is the only signal there is, and it is already folded into prior.
            return prior;
        }

        // Standard shrinkage blend — player has real sim data
        var blended = credibility * rawFppg + (1.0 - credibility) * prior;

        // Journeyman QB cap — Mayfield (31/exp8), Darnold (28/exp8), Goff tier.
        // Allen age 29 exp 7, Burrow age 29 exp 6 — NOT caught.
        if (position == "QB"
            && (player.Age ?? 0) >= 28
            && (player.YearsExperience ?? 0) >= 8)
        {
            blended = Math.Min(blended, 21.0);
        }

        // Starter threshold gate — experienced depth players pulled UP toward
        // prior get floored to depth level instead.
        // FAN-52: TE threshold is now 8.5 (was 6.0) — see StarterThreshold above.
        if ((player.YearsExperience ?? 0) >= 1)
        {
            var threshold = StarterThreshold.GetValueOrDefault(position, 7.0);
            if (blended < threshold)
                return GetDepthLevelFppg(position);
        }

        return blended;
    }

    private static double GetStarterAverageFppg(string position) => position switch
    {
        "QB" => 18.0,
        "RB" => 9.0,
        "WR" => 10.0,
        "TE" => 8.5,
        _ => 9.0
    };

    /// <summary>
    /// Deterministic 32-bit FNV-1a over the player id and season, used to seed
    /// the per-player RNG.
    ///
    /// Deliberately NOT <c>string.GetHashCode()</c>. .NET randomizes string
    /// hashing per process, so seeding from it would produce a simulation that
    /// looks reproducible, reads as reproducible, and quietly is not — the same
    /// shape as the silent no-ops this pipeline has already collected. FNV-1a is
    /// a few lines, has no dependency, and is stable across processes, machines
    /// and framework versions, which is the entire point.
    ///
    /// Season is folded in so a re-run for a different season draws a different
    /// career, while the same player and season always reproduce.
    /// </summary>
    private static int StableSeed(string sleeperPlayerId, int season)
    {
        const uint FnvOffsetBasis = 2166136261;
        const uint FnvPrime = 16777619;

        var hash = FnvOffsetBasis;

        foreach (var c in sleeperPlayerId)
        {
            hash ^= c;
            hash *= FnvPrime;
        }

        hash ^= (uint)season;
        hash *= FnvPrime;

        // Mask the sign bit rather than casting a value that may have the high
        // bit set — a negative seed is legal but makes the mapping depend on
        // two's-complement details for no benefit.
        return (int)(hash & 0x7FFFFFFF);
    }

    /// <summary>
    /// Whether a simulation row is a real historical season average, as opposed
    /// to a current-season projection that merely shares the Week-0 sentinel.
    ///
    /// Week 0 is overloaded: <c>SeedSeasonAverageSimsCommand</c> writes it for a
    /// season a player actually played, and the projection/simulation run writes
    /// it for the season ahead. Career simulation wants only the first kind. Joe
    /// Fagnano's Week-0 row (Median 0.17, PlayerRole "Unknown") is a projection
    /// for a quarterback who has never taken a snap, and reading it as a track
    /// record is what let him onto the dynasty board at all.
    ///
    /// Deliberately not an equality test on "SeasonAverage": rows written before
    /// the field existed carry no label, and excluding those would strip every
    /// baseline at once and turn the entire league into rookies. Keep the
    /// unlabelled, drop what is explicitly labelled something else.
    /// </summary>
    private static bool IsSeasonAverageRow(SimulationResultDocument r) =>
        r.Week == 0
        && (string.IsNullOrEmpty(r.PlayerRole) || r.PlayerRole == "SeasonAverage");

    /// <summary>
    /// Draft capital as a continuous signal rather than a yes/no gate — the
    /// share of the distance from depth level to the starter prior that a
    /// player's draft slot earns him before he has played a down.
    ///
    /// A 7th-round tight end is not a proven TE1; a 2nd-round quarterback is not
    /// a career backup. The previous pair of gates said otherwise in both
    /// directions, and disagreed with each other by position.
    ///
    /// Undrafted returns 0.0 — depth level, not zero. An undrafted rookie is a
    /// backup until proven otherwise, which is different from being worthless.
    /// </summary>
    private static double DraftPedigreeWeight(int? draftRound) => draftRound switch
    {
        1 => 1.00,
        2 => 0.80,
        3 => 0.55,
        4 or 5 => 0.30,
        6 or 7 => 0.15,
        _ => 0.00
    };

    private static double GetDepthLevelFppg(string position) => position switch
    {
        "QB" => 6.0,
        "RB" => 4.0,
        "WR" => 4.5,
        "TE" => 3.5,
        _ => 4.0
    };

    private static double GetAgingMultiplier(
        AgingCurveDocument? curve, string position, int age)
    {
        if (curve is null) return GetFallbackMultiplier(position, age);
        if (curve.AgeValueMap.TryGetValue(age, out var val)) return val / 100.0;
        return GetFallbackMultiplier(position, age);
    }

    private static double GetInjuryRisk(string position, int age)
    {
        var baseRisk = BaseInjuryRisk.GetValueOrDefault(position, 0.15);
        var peakAge = PeakAges.GetValueOrDefault(position, 26);
        var increment = AgeInjuryIncrement.GetValueOrDefault(position, 0.02);
        var yearsOver = Math.Max(0, age - peakAge);
        return Math.Min(0.65, baseRisk + yearsOver * increment);
    }

    private static CareerPhase ClassifyPhase(string position, int age)
    {
        var peak = PeakAges.GetValueOrDefault(position, 26);
        return age < peak - 2 ? CareerPhase.Ascending
            : age <= peak + 2 ? CareerPhase.Prime
            : age <= peak + 5 ? CareerPhase.Declining
            : CareerPhase.Unknown;
    }

    private static double GetPositionVariance(string position) => position switch
    {
        "QB" => 0.18,
        "RB" => 0.25,
        "WR" => 0.28,
        "TE" => 0.22,
        _ => 0.25
    };

    private static double GetFallbackMultiplier(string position, int age)
    {
        var peak = PeakAges.GetValueOrDefault(position, 26);
        if (age <= peak)
            return 0.6 + 0.4 * ((double)(age - 18) / (peak - 18));

        var window = PostPeakWindow.GetValueOrDefault(position, 8.0);
        return Math.Max(0.1, 1.0 - 0.9 * Math.Pow((double)(age - peak) / window, 2));
    }
}