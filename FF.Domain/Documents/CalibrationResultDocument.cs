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