using FF.Domain.Enums;

namespace FF.Domain.Documents;

public class CareerSimulationDocument
{
    public string Id { get; set; } = string.Empty;
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int CurrentAge { get; set; }
    public int Season { get; set; }                          // base season for simulation
    public CareerPhase CareerPhase { get; set; }
    public List<CareerYearProjection> YearProjections { get; set; } = [];
    public double CareerValueScore { get; set; }             // 0-100 normalized dynasty value
    public double PeakYearValue { get; set; }
    public int PeakYear { get; set; }
    public double YearsOfPrimeRemaining { get; set; }        // years above 70% of peak value
    public DateTime ComputedAt { get; set; }
    public int Iterations { get; set; }
}

public class CareerYearProjection
{
    public int Year { get; set; }
    public int AgeAtYear { get; set; }
    public double AgingMultiplier { get; set; }
    public double MedianFppg { get; set; }
    public double FloorFppg { get; set; }
    public double CeilingFppg { get; set; }
    public double InjuryRisk { get; set; }                   // 0.0-1.0 probability of significant injury
    public double ExpectedGamesPlayed { get; set; }          // 17 * (1 - injuryRisk)
    public double SeasonValue { get; set; }                  // MedianFppg * ExpectedGamesPlayed
    public CareerPhase Phase { get; set; }
}