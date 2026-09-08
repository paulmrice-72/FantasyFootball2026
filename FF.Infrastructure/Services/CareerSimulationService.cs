using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.Services;
using MathNet.Numerics.Distributions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using FF.Application.Services;

namespace FF.Infrastructure.Services;

public class CareerSimulationService(
    IPlayerRepository playerRepository,
    IAgingCurveRepository agingCurveRepository,
    ISimulationResultRepository simulationResultRepository,
    IDepthChartRepository depthChartRepository,
    ILogger<CareerSimulationService> logger) : ICareerSimulationService
{
    private const int Iterations = 1000;
    private const int ProjectYears = 5;
    private const int CurrentSeason = 2026;

    // ── FAN-168: weight on the most recent of the two merged seasons ────────
    //
    // The merge below used to average the two most recent seasons equally, to
    // stop "one outlier season (Darnold 2024: 18.1) from seeding an inflated
    // 5-year projection". Sound goal; a 50/50 average cannot tell an outlier
    // from a trend, and it fails in the direction that hurts ascending players.
    //
    // Measured 2026-09-08 at TE, where the failure was total because the
    // shrinkage gate downstream was a cliff:
    //   David Njoku  10.59 → 6.35  (declining)  merged 8.47 → cleared, model TE1
    //   Kyle Pitts    6.34 → 9.81  (ascending)  merged 8.08 → floored to 3.5
    // Both defects are fixed in this change. This constant is the first half.
    //
    // 0.5 reproduces the previous behaviour EXACTLY, which is deliberate: the
    // gate removal and this weighting land together, so setting this to 0.5 and
    // re-running calibration isolates one from the other without splitting the
    // change. Do that before tuning it — a weight moved against a number that
    // is also absorbing the gate fix is a weight tuned against nothing.
    private const decimal RecentSeasonWeight = 0.65m;

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
    // FAN-157 / FAN-95, 2026-09-07 — READ BEFORE TUNING THIS.
    //
    // The justification above describes correcting "a single population-average
    // polynomial fit across ALL players at a position." That fit did not exist
    // when this constant was chosen: aging_curves had never been built, so
    // GetAgingMultiplier fell through to the hardcoded fallback for every
    // player, always. 0.6 was tuned to counteract a curve that never loaded.
    //
    // It is now operating on real curves — and, after FAN-157, on curves built
    // by a longitudinal estimator rather than a cross-sectional one, which is a
    // second change of input in as many weeks. Whatever this constant is doing
    // today, nobody has measured it.
    //
    // To measure: set to 0.0, rebuild, re-run career sims → breakout → DFV, and
    // run the calibration harness on the Model basis (FAN-159). Compare against
    // the same run at 0.6. Do that BEFORE re-deriving it, or the dampening will
    // absorb part of the aging correction and hide whether the new curves
    // helped. A recompile toggle rather than a setting on purpose — the whole
    // pipeline has to be re-run either way, so config plumbing buys nothing.
    private const double EliteDecayDampening = 0.6;

    /// <summary>
    /// A projected year counts as "prime" when its SeasonValue is at least this
    /// fraction of the player's own peak projected year. See the note where
    /// YearsOfPrimeRemaining is computed for why this is self-relative.
    /// </summary>
    private const double PrimeValueFraction = 0.70;

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

    // FAN-157: PeakAges and PostPeakWindow lived here and, in slightly
    // different form, in AgingCurveService — four dictionaries across two
    // projects describing one thing. Both now come from
    // AgingFallbackCurve.WindowFor(position), which carries the same values
    // this file has always used (QB 29 / RB 24 / WR 26 / TE 27, decline windows
    // 8 / 5 / 9 / 8, default peak 26). No shape change; one place to edit.
    private static int PeakAgeFor(string position) =>
        AgingFallbackCurve.WindowFor(position).PeakAge;

    // ── FAN-168: the prior is conditioned on depth-chart role ───────────────
    //
    // PositionPriors above is documented as "average STARTER FPPG by position"
    // and used to be applied to every player regardless of role. Shrinking a
    // backup toward a starter prior inflates him, so two gates existed to undo
    // that — a pre-blend `rawFppg >= depthLevel` test and a post-blend
    // `StarterThreshold` test, both of which returned the flat depth level.
    // Both are deleted here, because both were patching a prior that should not
    // have needed patching.
    //
    // What they cost, measured against dev on 2026-09-08. At TE the post-blend
    // threshold (8.5) sat ABOVE the prior (9.0), so clearing it required raw
    // >= 8.2 FPPG and everything below dropped to a flat 3.5:
    //
    //   Kyle Pitts     raw 8.08 → blended 8.422 → missed by 0.078 → 3.5
    //   Mark Andrews   raw 7.89 → blended 8.306 → missed by 0.194 → 3.5
    //   T.J. Hockenson raw 6.21 → blended 7.256 →                  → 3.5
    //   David Njoku    raw 8.47 → blended 8.669 → cleared by 0.169 → 8.67
    //
    // Eight tight ends kept a real baseline; every other one in the position
    // collapsed onto 3.5, after which age was the only thing left to sort on.
    // Within-position Spearman at TE was 0.213 against 0.66 at QB and WR.
    //
    // The prior having been above the threshold also made the gate run backwards
    // on evidence: a 1-year TE needed raw 7.0 to pass, a 3-year TE 8.0, a
    // veteran 8.2. The less the model knew about a player, the easier the bar.
    //
    // Role weight is expressed the same way DraftPedigreeWeight is — the share
    // of the distance from depth level to the starter prior that a player's
    // role earns him — so the two compose for rookies and read the same way.
    private static double DepthRoleWeight(int? depthTeam)
    {
        // Unknown depth shrinks toward the midpoint of the two priors rather
        // than either end. DepthRoleAdjustment resolves a missing row to 1.0 on
        // the grounds that absence of evidence is not evidence of being a
        // backup, and that is right for a MULTIPLIER applied to an observed
        // projection — being wrong there costs a player 75% of a real number.
        // A prior is the opposite case: it is what we fall back on when we have
        // little evidence, so answering "we do not know" with the starter's
        // number is a claim, not a neutral default. 0.5 is the neutral one.
        //
        // Deliberately not branching on the player's own production to guess a
        // role: that reintroduces a discontinuity at exactly the boundary this
        // change exists to remove.
        if (depthTeam is null || depthTeam <= 0) return 0.50;

        return depthTeam.Value switch
        {
            1 => 1.00,
            2 => 0.35,
            _ => 0.10
        };
    }

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
                        // FAN-168: weighted toward the recent season, not a flat
                        // mean. See RecentSeasonWeight — 0.5 restores the old
                        // behaviour exactly.
                        Median = Weighted(recent.Median, prior.Median),
                        Floor = Weighted(recent.Floor, prior.Floor),
                        Ceiling = Weighted(recent.Ceiling, prior.Ceiling),
                        Mean = Weighted(recent.Mean, prior.Mean),
                        BaseProjection = Weighted(recent.BaseProjection, prior.BaseProjection),
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
                        // FAN-168: weighted toward the recent season, not a flat
                        // mean. See RecentSeasonWeight — 0.5 restores the old
                        // behaviour exactly.
                        Median = Weighted(recent.Median, prior.Median),
                        Floor = Weighted(recent.Floor, prior.Floor),
                        Ceiling = Weighted(recent.Ceiling, prior.Ceiling),
                        Mean = Weighted(recent.Mean, prior.Mean),
                        BaseProjection = Weighted(recent.BaseProjection, prior.BaseProjection),
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

            // FAN-168: depth-chart role, bulk-loaded per position. The prior a
            // player shrinks toward depends on it, so it has to be resolved
            // before the elite-tier ranking as well — that ranking is itself
            // built on shrunk baselines.
            var depthByPlayerId = await LoadDepthRolesAsync(posStr, season, ct);

            var eliteTierByPlayerId = BuildEliteTierMap(
                players, posStr, simByPlayerId, simByNamePos, depthByPlayerId);

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
                        simByPlayerId, simByNamePos, eliteTier,
                        depthByPlayerId.TryGetValue(player.SleeperPlayerId, out var dt) ? dt : null);
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
        // FAN-168: same depth-role resolution the bulk path uses, for the same
        // reason the elite-tier map is rebuilt here — a single-player recompute
        // that shrank toward a different prior than the nightly run would put
        // two disagreeing numbers on the same player with nothing to say which
        // ran last.
        var depthByPlayerId = await LoadDepthRolesAsync(posStr, CurrentSeason, ct);

        var eliteTierByPlayerId = BuildEliteTierMap(
            positionPlayers, posStr, simByPlayerId, simByNamePos, depthByPlayerId);
        var eliteTier = eliteTierByPlayerId.GetValueOrDefault(sleeperPlayerId, 0.0);

        return SimulatePlayer(
            player, posStr, curve, CurrentSeason, simByPlayerId, simByNamePos, eliteTier,
            depthByPlayerId.TryGetValue(sleeperPlayerId, out var dt) ? dt : null);
    }

    /// <summary>
    /// FAN-168. Most recent depth-chart row per player at a position, as
    /// <c>SleeperPlayerId → DepthTeam</c>.
    ///
    /// <para>
    /// Coverage is logged rather than assumed. This lookup decides which prior
    /// every player at the position shrinks toward, so a season with no depth
    /// chart synced would silently put the whole position on the unknown-role
    /// weight — which is a coherent answer, but a different model from the one
    /// intended, and it should be visible in the log rather than inferred later
    /// from a calibration number that moved less than expected.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, int>> LoadDepthRolesAsync(
        string position, int season, CancellationToken ct)
    {
        var rows = await depthChartRepository.GetLatestByPositionAsync(position, season, ct);

        var map = rows
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId) && r.DepthTeam > 0)
            .GroupBy(r => r.SleeperPlayerId)
            .ToDictionary(g => g.Key, g => g.Min(r => r.DepthTeam));

        logger.LogInformation(
            "Depth roles for {Position} season {Season}: {Mapped} players mapped " +
            "({Starters} at depth 1, {Backups} at depth 2, {Deep} at depth 3+)",
            position, season, map.Count,
            map.Values.Count(d => d == 1),
            map.Values.Count(d => d == 2),
            map.Values.Count(d => d >= 3));

        return map;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private CareerSimulationDocument SimulatePlayer(
        FF.Domain.Entities.Player player,
        string position,
        AgingCurveDocument? curve,
        int season,
        Dictionary<string, SimulationResultDocument> simByPlayerId,
        Dictionary<string, SimulationResultDocument> simByNamePos,
        double eliteTierFactor = 0.0,
        int? depthTeam = null)
    {
        var currentAge = player.Age ?? (player.YearsExperience == 0 ? 21 : 22);
        var peakAgeForPosition = curve?.PeakAge ?? PeakAgeFor(position);

        var rawBaseline = GetBaselineFppg(
            player.SleeperPlayerId!, player.FullName, position,
            simByPlayerId, simByNamePos);

        var baseFppg = ApplyShrinkage(position, rawBaseline, player, depthTeam);

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

        // FAN-157: these two used to be computed from different quantities —
        // PeakYear from SeasonValue, YearsOfPrimeRemaining from the aging
        // multiplier alone — so they could and did contradict each other. Seven
        // of the production top 25 carried "peak year 2030, prime years 0",
        // which is incoherent on its face: the peak year is by definition a year
        // worth having.
        //
        // Both now derive from SeasonValue, and prime is measured relative to
        // the player's own peak: how many of the projected years he is within
        // 30% of his own ceiling. The peak year scores 100% of itself, so it is
        // always inside the prime count and the contradiction is structurally
        // impossible rather than merely fixed.
        //
        // This changes what the PRIME YRS column means. It is no longer "years
        // until he stops being good" (an absolute bar) but "years he holds near
        // his own best" — so a 33-year-old reads 1 rather than 0, which is the
        // honest answer to the question the column actually asks.
        var peakYear = yearProjections.MaxBy(y => y.SeasonValue)!;
        var primeThreshold = peakYear.SeasonValue * PrimeValueFraction;
        var primeYears = yearProjections.Count(y => y.SeasonValue >= primeThreshold);

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
        Dictionary<string, SimulationResultDocument> simByNamePos,
        Dictionary<string, int> depthByPlayerId)
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
            var blended = ApplyShrinkage(
                position, raw, player,
                depthByPlayerId.TryGetValue(player.SleeperPlayerId, out var dt) ? dt : null);
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
        FF.Domain.Entities.Player player,
        int? depthTeam)
    {
        var starterPrior = PositionPriors.GetValueOrDefault(position, 9.0);
        var depthLevel = GetDepthLevelFppg(position);
        var clampedExp = Math.Min(player.YearsExperience ?? 0, 5);
        var credibility = clampedExp / (clampedExp + ShrinkageK);

        // FAN-168. The prior is now the one for this player's ROLE, so nothing
        // downstream has to undo an inflated starter prior. See DepthRoleWeight.
        var prior = depthLevel + DepthRoleWeight(depthTeam) * (starterPrior - depthLevel);

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
        // A rookie's credibility is zero, which means the prior IS his
        // projection. Scale that prior by draft capital so an undrafted rookie
        // cannot inherit the median starter's season simply by existing in the
        // player table.
        //
        // FAN-168: this now composes with the role weight rather than replacing
        // it — a 1st-round rookie buried at third string does not get a
        // starter's prior on draft capital alone, and an undrafted rookie who is
        // already the listed starter is not held at the floor by his draft slot.
        if ((player.YearsExperience ?? 0) == 0)
        {
            prior = depthLevel + (DraftPedigreeWeight(player.DraftRound) * (prior - depthLevel));
        }

        // FAN-168: the two starter gates that used to sit here are gone.
        //
        // The first tested `rawFppg >= depthLevel` and returned the flat depth
        // level for anyone below it. Read as a floor it looks protective; what
        // it actually did was RAISE weak players to a constant — Jared Wiley
        // produces 0.83 FPPG and was projected at 3.5 — which is half of how the
        // tie block formed. The second tested the blend against
        // StarterThreshold and returned the same constant, which is the other
        // half and the larger one.
        //
        // Neither is needed once the prior matches the role: a third-string tight
        // end now shrinks toward 4.05 instead of 9.0, so there is nothing
        // inflated left to catch. What replaces both is the blend itself, which
        // is continuous and monotone in production — the property a rank
        // correlation actually rewards, and the one the gates destroyed.
        var blended = credibility * rawFppg + (1.0 - credibility) * prior;

        // Journeyman QB cap — Mayfield (31/exp8), Darnold (28/exp8), Goff tier.
        // Allen age 29 exp 7, Burrow age 29 exp 6 — NOT caught.
        if (position == "QB"
            && (player.Age ?? 0) >= 28
            && (player.YearsExperience ?? 0) >= 8)
        {
            blended = Math.Min(blended, 21.0);
        }

        return blended;
    }

    /// <summary>
    /// FAN-168. Blend of the two most recent seasons, weighted toward the recent
    /// one. Kept as one helper so the five fields cannot drift apart — Median
    /// weighted differently from BaseProjection would be invisible until someone
    /// compared two columns of the same row.
    /// </summary>
    private static decimal Weighted(decimal recent, decimal prior) =>
        Math.Round(recent * RecentSeasonWeight + prior * (1m - RecentSeasonWeight), 2);

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
        var peakAge = PeakAgeFor(position);
        var increment = AgeInjuryIncrement.GetValueOrDefault(position, 0.02);
        var yearsOver = Math.Max(0, age - peakAge);
        return Math.Min(0.65, baseRisk + yearsOver * increment);
    }

    private static CareerPhase ClassifyPhase(string position, int age)
    {
        var peak = PeakAgeFor(position);
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

    // FAN-157: this used to be a second, subtly different copy of
    // AgingCurveService.GetDefaultMultiplier — same intent, ascending from a
    // hardcoded 18 instead of the position's window minimum, so the two
    // services returned different multipliers for the same young player. One
    // function now, in FF.Domain. The ascent base is the window minimum; see
    // AgingFallbackCurve for why that side of the disagreement won.
    private static double GetFallbackMultiplier(string position, int age)
        => AgingFallbackCurve.Multiplier(position, age);
}