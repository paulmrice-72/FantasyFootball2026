// FF.Domain/Entities/BriefDeliveryPreference.cs
namespace FF.Domain.Entities;

public class BriefDeliveryPreference
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; } = true;
    public int DeliveryDayOfWeek { get; set; } = 0; // 0 = Sunday
    public int DeliveryHourUtc { get; set; } = 8;
    public bool IncludeBoomCandidates { get; set; } = true;
    public bool IncludeBustRisks { get; set; } = true;
    public bool IncludeLeagueSections { get; set; } = true;
    public bool IncludeCoachRiley { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string TimeZoneId { get; set; } = "America/Chicago";
}