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
    public int SampleSize { get; set; }                          // number of player-seasons used
    public bool IsDefaultCurve { get; set; }
}