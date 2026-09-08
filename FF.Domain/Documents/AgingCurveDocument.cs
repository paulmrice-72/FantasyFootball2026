using FF.Domain.Enums;

namespace FF.Domain.Documents;

public class AgingCurveDocument
{
    public string Id { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;         // QB, RB, WR, TE
    public double[] Coefficients { get; set; } = [];             // Polynomial coefficients (degree 3)
    public int PeakAge { get; set; }
    public double PeakValue { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; }
    public Dictionary<int, double> AgeValueMap { get; set; } = []; // age → normalized value 0-100
    public DateTime ComputedAt { get; set; }

    /// <summary>
    /// Number of year-over-year player transitions the fit consumed (FAN-157).
    /// Before the switch to a longitudinal estimator this held the raw game-log
    /// count, which overstated the sample by roughly an order of magnitude and
    /// did not match this field's own description.
    /// </summary>
    public int SampleSize { get; set; }
    public bool IsDefaultCurve { get; set; }
}