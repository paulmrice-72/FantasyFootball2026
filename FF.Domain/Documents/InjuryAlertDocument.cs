namespace FF.Domain.Documents;

public class InjuryAlertDocument
{
    public string Id { get; set; } = string.Empty;
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string? NflTeam { get; set; }
    public string Designation { get; set; } = string.Empty; // Questionable/Doubtful/Out/IR
    public DateTime SyncedAt { get; set; }
}