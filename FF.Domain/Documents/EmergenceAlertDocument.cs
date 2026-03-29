// FF.Domain/Documents/EmergenceAlertDocument.cs

namespace FF.Domain.Documents;

public enum EmergenceTriggerSignal
{
    SnapShareSurge,
    TargetShareSurge,
    CarryShareSurge,
    WoprSpike
}

public class EmergenceAlertDocument
{
    public string? Id { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string? NflTeam { get; set; }
    public EmergenceTriggerSignal TriggerSignal { get; set; }
    public decimal Delta { get; set; }
    public int Week { get; set; }
    public int Season { get; set; }
    public DateTime DetectedAt { get; set; }
    public bool IsAcknowledged { get; set; }
}