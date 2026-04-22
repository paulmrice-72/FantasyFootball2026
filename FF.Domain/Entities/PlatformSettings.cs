namespace FF.Domain.Entities;

public class PlatformSettings
{
    public int Id { get; set; } // Always row 1
    public bool RegistrationsEnabled { get; set; } = true;
    public bool AiJobsEnabled { get; set; } = false; // Default OFF — off-season safe
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}