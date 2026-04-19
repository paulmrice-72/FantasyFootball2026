using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FF.Infrastructure.Persistence.SQL.Configurations;

public class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("platform_settings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RegistrationsEnabled).HasColumnName("registrations_enabled");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(256);

        // Seed the one-and-only row so GetAsync() always finds something
        builder.HasData(new PlatformSettings
        {
            Id = 1,
            RegistrationsEnabled = true,
            UpdatedAt = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc),
            UpdatedBy = "system"
        });
    }
}