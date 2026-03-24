// FF.Infrastructure/Persistence/SQL/Repositories/BriefDeliveryPreferenceRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class BriefDeliveryPreferenceRepository(FFDbContext db)
    : IBriefDeliveryPreferenceRepository
{
    public async Task<BriefDeliveryPreference?> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        return await db.BriefDeliveryPreferences
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
    }

    public async Task UpsertAsync(
        BriefDeliveryPreference preference, CancellationToken ct = default)
    {
        var existing = await db.BriefDeliveryPreferences
            .FirstOrDefaultAsync(x => x.UserId == preference.UserId, ct);

        if (existing is null)
        {
            preference.Id = Guid.NewGuid();
            preference.CreatedAt = DateTime.UtcNow;
            db.BriefDeliveryPreferences.Add(preference);
        }
        else
        {
            existing.EmailEnabled = preference.EmailEnabled;
            existing.DeliveryDayOfWeek = preference.DeliveryDayOfWeek;
            existing.DeliveryHourUtc = preference.DeliveryHourUtc;
            existing.IncludeBoomCandidates = preference.IncludeBoomCandidates;
            existing.IncludeBustRisks = preference.IncludeBustRisks;
            existing.IncludeLeagueSections = preference.IncludeLeagueSections;
            existing.IncludeCoachRiley = preference.IncludeCoachRiley;
            existing.TimeZoneId = preference.TimeZoneId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}