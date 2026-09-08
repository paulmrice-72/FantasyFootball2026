using FF.Domain.Enums;

namespace FF.Domain.Documents;

public class DynastyValuationDocument
{
    public string Id { get; set; } = string.Empty;
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public int Age { get; set; }
    public int? YearsExperience { get; set; }
    public int Season { get; set; }

    // ── Scoring format this valuation was calculated for ─────────────────
    public ScoringFormat ScoringFormat { get; set; } = ScoringFormat.HalfPpr;

    // ── Breakout Signal ──────────────────────────────────────────────────
    public double BreakoutScore { get; set; }
    public BreakoutClassification BreakoutClassification { get; set; }
    public List<string> BreakoutSignals { get; set; } = [];
    public DateTime BreakoutScoredAt { get; set; }

    // ── Discounted Future Value ──────────────────────────────────────────
    public double TradeValue { get; set; }
    public double DiscountedFutureValue { get; set; }
    public DateTime? TradeValueComputedAt { get; set; }

    /// <summary>
    /// FAN-159. The same pipeline's answer with every FantasyPros-derived step
    /// removed — normalization plus our own positional guardrails, and nothing
    /// else. This is what the model actually thinks a player is worth.
    ///
    /// <para>
    /// <see cref="TradeValue"/> is NOT that. For any player FantasyPros ranks it
    /// is 35% model and 65% FantasyPros rank (15/85 for tight ends), because the
    /// DFV pipeline blends toward an anchor that is a pure step function of FP's
    /// dynasty rank. That blend is deliberate and stays — FP consensus is a good
    /// prior, and TradeValue is what the product should serve. The problem it
    /// created is measurement: the calibration harness ranked players by
    /// TradeValue and scored the result against FantasyPros rank, so it was
    /// grading FantasyPros against itself at 65% weight. A model producing pure
    /// noise would still have posted a substantial ρ, because the blend alone
    /// supplied most of the ordering.
    /// </para>
    ///
    /// <para>
    /// Which steps count as "ours" is a judgement, and this is where the line was
    /// drawn: the rank-based normalization curve and the QB/RB/WR/TE tier
    /// guardrails are our own priors and stay in; the FP dynasty blend, the FP
    /// anchor term inside the TE cap, and the FP-rookie-rank floor are borrowed
    /// opinion and come out. The rookie floor only ever fires for players with no
    /// FP dynasty row, and those players are dropped by the harness's own join
    /// anyway, so excluding it changes nothing that is measured.
    /// </para>
    ///
    /// <para>
    /// Nothing on the site reads this field — it exists so the harness has an
    /// honest thing to rank. Expect it to score worse than TradeValue does. That
    /// is the point: the old number was measuring the blend.
    /// </para>
    /// </summary>
    public double ModelValue { get; set; }

    /// <summary>
    /// FAN-166. The same pipeline one step earlier than <see cref="ModelValue"/>:
    /// post-normalization, and before the positional guardrail caps.
    ///
    /// <para>
    /// FAN-159 drew the line at provenance — the guardrails are ours, so they
    /// stayed in the model track. Measurement on 2026-09-08 showed that decision
    /// carries more weight than intended. The cap tables were fitted against the
    /// blended distribution, where the FantasyPros blend had already corrected
    /// each player's positional rank before the caps landed. Applied instead to
    /// the model's own uncorrected rank order they bind in completely different
    /// places: A.J. Brown is WR ~25 blended and WR 48 on the model track, which
    /// puts him on opposite sides of the 45/46 cliff in
    /// <c>GetWrGuardrailCap</c> — 65 against 35.
    /// </para>
    ///
    /// <para>
    /// The effect is not marginal. Sorted by raw DFV, A.J. Brown is 428 and
    /// Jeremy Ruckert is 195; sorted by ModelValue, Ruckert is ahead. A single
    /// global sort cannot invert an ordering, so the inversion is entirely
    /// <c>ApplyPositionalGuardrails</c>, which is the only stage that partitions
    /// by position. For everyone outside roughly a position's top 12-20 the
    /// stamped value is <c>cap - rankFraction * bandWidth</c> and the model
    /// contributes only the ordering inside a 4-point band.
    /// </para>
    ///
    /// <para>
    /// This field exists so that can be measured rather than argued about: run
    /// the harness on Raw, Model and Blended from the same run and the
    /// guardrails' contribution to rho is the difference between the first two.
    /// Nothing on the site reads it.
    /// </para>
    /// </summary>
    public double RawValue { get; set; }

    // ── Career Sim reference ─────────────────────────────────────────────
    public double CareerValueScore { get; set; }
    public int PeakYear { get; set; }
    public double YearsOfPrimeRemaining { get; set; }
    public CareerPhase CareerPhase { get; set; }
}