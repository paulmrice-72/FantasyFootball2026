namespace FF.Domain.Documents;

public class SnapCountDocument
{
    public string? Id { get; set; }
    public string PfrPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Week { get; set; }
    public int OffenseSnaps { get; set; }
    public decimal OffensePct { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}