using FF.Application.Interfaces.Persistence;
using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class PlatformSettingsRepository(FFDbContext db) : IPlatformSettingsRepository
{
    public async Task<PlatformSettings> GetAsync()
    {
        // Always row 1 — seeded by migration. Create defensively if somehow missing.
        var row = await db.PlatformSettings.FirstOrDefaultAsync();
        if (row is not null) return row;

        row = new PlatformSettings
        {
            Id = 1,
            RegistrationsEnabled = true,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "system"
        };
        db.PlatformSettings.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    public async Task SaveAsync(PlatformSettings settings)
    {
        settings.UpdatedAt = DateTime.UtcNow;
        db.PlatformSettings.Update(settings);
        await db.SaveChangesAsync();
    }
}