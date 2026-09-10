// FF.Domain/Documents/CalibrationResultDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// Stores the output of one calibration harness run.
/// Collection: calibration_results
/// </summary>
public class CalibrationResultDocument
{
    public string Id { get; set; } = string.Empty;
    public DateTime RunAt { get; set; }
    public string ScoringFormat { get; set; } = "Superflex";

    /// <summary>
    /// FAN-159, extended by FAN-166. Which of our three values this run ranked
    /// players by: "Raw" (RawValue — post-normalization, before the positional
    /// guardrail caps), "Model" (ModelValue — the pipeline with every
    /// FantasyPros-derived step removed but the caps applied) or "Blended"
    /// (TradeValue — what the site serves, 65% FantasyPros rank).
    ///
    /// <para>
    /// This is not a display detail; it decides whether the numbers beside it
    /// mean anything. On the Blended basis the harness ranks by a value that is
    /// mostly FantasyPros rank and then scores it against FantasyPros rank, so ρ
    /// is inflated by construction and a model producing noise would still post
    /// a high one.
    /// </para>
    ///
    /// <para>
    /// Rows written before 2026-09-07 have no value here and were all measured
    /// on the blended basis. Read a missing value as "Blended", and do not
    /// compare a pre-2026-09-07 ρ against a post-2026-09-07 one — they are
    /// measuring different things, and the drop between them is the correction,
    /// not a regression.
    /// </para>
    /// </summary>
    public string ValueBasis { get; set; } = "Blended";

    /// <summary>
    /// FAN-166. The single position this run was restricted to ("QB", "RB",
    /// "WR", "TE"), or null for the whole board.
    ///
    /// <para>
    /// A whole-board ρ is two different questions added together: does the
    /// pipeline get the cross-position ladder right, and does it order players
    /// correctly inside a position. On 2026-09-08 those two had opposite signs —
    /// the Raw basis scored 0.8047 against Model's 0.5875 while simultaneously
    /// putting Blake Bortles in the top 15% of the board — so the combined
    /// number could not say which half was moving.
    /// </para>
    ///
    /// <para>
    /// Restricting to one position removes the cross-position ladder from the
    /// comparison entirely: both series are dense-ranked within the matched
    /// subset, so what is left is purely within-position ordering. Run the same
    /// basis with and without a position and the difference is the ladder.
    /// </para>
    /// </summary>
    public string? Position { get; set; }

    /// <summary>Spearman rank-order correlation vs FantasyPros top-200. Target ≥ 0.85.</summary>
    public double SpearmanRho { get; set; }

    /// <summary>Mean absolute rank delta across top-200. Target ≤ 8.0.</summary>
    public double AvgAbsDelta { get; set; }

    /// <summary>Count of our top-10 that appear in FP top-10. Target ≥ 7.</summary>
    public int Top10Overlap { get; set; }

    /// <summary>Snapshot of the top-20 comparison for display.</summary>
    public List<CalibrationPlayerSnapshot> Top20Snapshot { get; set; } = [];

    /// <summary>
    /// The twenty largest rank disagreements anywhere in the compared population,
    /// worst first. Added 2026-09-07 because the top-20 view was systematically
    /// misleading about where the error lives: measured on the 09-06 runs, mean
    /// |Δ| across the visible top 20 was 6.8 — inside the 8.0 target — while the
    /// remaining ~170 players averaged 22.4. Tuning against the top 20 optimises
    /// the tenth of the list that already works.
    /// </summary>
    public List<CalibrationPlayerSnapshot> Worst20Snapshot { get; set; } = [];

    /// <summary>Total players compared.</summary>
    public int PlayerCount { get; set; }

    /// <summary>
    /// How many of our valuations were thrown away because FantasyPros has no
    /// row for them. Added 2026-09-07: this join was silent, and the players it
    /// discards are not random — Patrick Mahomes, our #1, was one of them, which
    /// is how the calibration table and the Dynasty Rankings page came to
    /// disagree about who leads the board. Every metric here is computed on
    /// whatever survived, so this number is part of reading them.
    /// </summary>
    public int UnmatchedCount { get; set; }

    /// <summary>
    /// The highest-valued players we dropped, most valuable first. These are the
    /// ones whose absence distorts the comparison most.
    /// </summary>
    public List<string> TopUnmatched { get; set; } = [];

    /// <summary>
    /// FAN-175. How many valuations the selection returned, against
    /// <see cref="RequestedCount"/>.
    ///
    /// <para>
    /// These were assumed equal and were not. The three <c>GetTopBy*ValueAsync</c>
    /// selections took the top N by value with no scored filter, so a position with
    /// fewer than N scored players had its graded population padded out of the
    /// zeroed tail — measured 2026-09-10, 115 scored QBs, 189 RBs, 201 TEs and 323
    /// WRs against a request for 250. Those padding rows all held exactly
    /// <c>0.0</c> and the sort carried no secondary key, so which of several hundred
    /// tied documents came back was arbitrary and differed between runs over
    /// identical data. That is the moving population, and this pair of numbers is
    /// what makes it visible on the face of a result instead of two days later.
    /// </para>
    /// </summary>
    public int SelectedCount { get; set; }

    /// <summary>FAN-175. The top-N the selection asked for.</summary>
    public int RequestedCount { get; set; }

    /// <summary>
    /// FAN-175. The Sleeper ids this run actually graded, in ranked order.
    ///
    /// <para>
    /// Two runs' rho are comparable only if this list is. Nothing recorded it until
    /// now, so every within-position comparison the project has published — FAN-168's
    /// and FAN-170's tables among them — rested on an assumption that could not be
    /// checked and turned out to be false. With this stored the check is a set
    /// difference against a previous run rather than an argument about whether the
    /// population could have moved.
    /// </para>
    /// </summary>
    public List<string> MatchedPlayerIds { get; set; } = [];
}

public class CalibrationPlayerSnapshot
{
    public int OurRank { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public double OurTradeValue { get; set; }

    /// <summary>FantasyPros' rank in THEIR full list — useful for looking a player up.</summary>
    public int FpRank { get; set; }

    /// <summary>
    /// FantasyPros' rank re-expressed within the matched subset, which is the
    /// only basis on which a comparison to OurRank means anything. Both series
    /// are then dense 1..n rankings over the same population.
    /// </summary>
    public double FpSubsetRank { get; set; }

    /// <summary>
    /// OurRank − FpSubsetRank (positive = we rank him higher than FP does).
    ///
    /// Computed on the SUBSET rank, not the raw one. It used to use FpRank while
    /// the headline Avg |Δ| used the subset rank, so the column and the summary
    /// statistic were measuring different things and the column could not be
    /// averaged to reach the number displayed above it.
    /// </summary>
    public double Delta { get; set; }
}