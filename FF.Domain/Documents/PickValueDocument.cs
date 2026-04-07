// FF.Domain/Documents/PickValueDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// Stores dynasty draft pick trade values by round, tier, and year.
/// Collection: pick_values
/// Seeded by SeedPickValuesJob on first run. Admin-editable in future Epic 13.
/// </summary>
public class PickValueDocument
{
    public string Id { get; set; } = string.Empty;
    public int Round { get; set; }
    public string Tier { get; set; } = string.Empty;  // "Early", "Mid", "Late"
    public int Year { get; set; }
    public double Value { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}