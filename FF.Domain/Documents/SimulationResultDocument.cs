// FF.Domain/Documents/SimulationResultDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// Stores the Monte Carlo simulation output for one player/week.
/// One document per player per season per week — upserted on each simulation run.
/// Collection: simulation_results
/// </summary>
public class SimulationResultDocument
{
    public string Id { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string? SleeperPlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public string OpponentTeam { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Week { get; set; }

    // Simulation inputs
    public int Iterations { get; set; }
    public decimal BaseProjection { get; set; }    // median from regression model
    public decimal StandardDeviation { get; set; } // derived from historical variance

    // Distribution outputs (PPR)
    public decimal Floor { get; set; }             // 10th percentile
    public decimal Median { get; set; }            // 50th percentile
    public decimal Ceiling { get; set; }           // 90th percentile
    public decimal Mean { get; set; }              // arithmetic mean of all iterations

    // Boom/bust probabilities
    public decimal BoomProbability { get; set; }   // P(score >= 2x baseline)
    public decimal BustProbability { get; set; }   // P(score <= 0.5x baseline)

    // Role context — from RoleClassificationService
    public string PlayerRole { get; set; } = "Unknown";

    /// <summary>
    /// Games behind this number. Zero on rows that predate the field or that were
    /// not built from a counted sample. Added 2026-09-07 for the season-average
    /// seed, where a two-game sample and a seventeen-game one were previously
    /// indistinguishable once divided — which is how one afternoon became a
    /// season-long rate. Store it so a consumer can weight or filter rather than
    /// having to trust every average equally.
    /// </summary>
    public int GameSampleSize { get; set; }

    public string ScoringFormat { get; set; } = "HalfPpr";
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    // Vegas context — stamped from VegasLineDocument at simulation time
    public decimal Spread { get; set; } = 0m;
    public string GameScript { get; set; } = "Unknown";
}