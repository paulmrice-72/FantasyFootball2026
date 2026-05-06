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

    /// <summary>Total players compared.</summary>
    public int PlayerCount { get; set; }
}

public class CalibrationPlayerSnapshot
{
    public int OurRank { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public double OurTradeValue { get; set; }
    public int FpRank { get; set; }
    public int Delta { get; set; } // OurRank - FpRank (positive = we rank higher than FP)
}