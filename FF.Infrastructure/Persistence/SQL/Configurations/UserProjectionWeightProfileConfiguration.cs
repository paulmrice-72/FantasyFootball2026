// FF.Infrastructure/Persistence/Configurations/UserProjectionWeightProfileConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FF.Domain.Entities;

namespace FF.Infrastructure.Persistence.SQL.Configurations;

public class UserProjectionWeightProfileConfiguration
    : IEntityTypeConfiguration<UserProjectionWeightProfile>
{
    public void Configure(EntityTypeBuilder<UserProjectionWeightProfile> builder)
    {
        builder.ToTable("UserProjectionWeightProfiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AppUserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.ProfileName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.RecentGameWeight).HasPrecision(5, 4);
        builder.Property(x => x.SnapCountWeight).HasPrecision(5, 4);
        builder.Property(x => x.MatchupWeight).HasPrecision(5, 4);
        builder.HasIndex(x => new { x.AppUserId, x.IsActive });
    }
}