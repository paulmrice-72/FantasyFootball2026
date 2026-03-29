// FF.Domain/Documents/VorpRecommendationDocument.cs

namespace FF.Domain.Documents;

public class VorpRecommendationDocument
{
    public string? Id { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string? NflTeam { get; set; }
    public int Season { get; set; }
    public int Week { get; set; }
    public decimal ProjectedPoints { get; set; }
    public decimal ReplacementLevel { get; set; }
    public decimal Vorp { get; set; }
    public decimal FloorPoints { get; set; }
    public decimal CeilingPoints { get; set; }
    public int VorpRank { get; set; }
    public int PositionRank { get; set; }
    public DateTime ComputedAt { get; set; }
}