// FF.Domain/Documents/PlayerNarrativeDocument.cs
namespace FF.Domain.Documents;

public class PlayerNarrativeDocument
{
    public string? Id { get; set; }
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Narrative { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}