using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FF.Infrastructure.Persistence.SQL.Configurations;

public class LeagueConfiguration : IEntityTypeConfiguration<League>
{
    public void Configure(EntityTypeBuilder<League> builder)
    {
        builder.ToTable("Leagues");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .ValueGeneratedNever();

        builder.Property(l => l.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(l => l.SleeperLeagueId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(l => l.Season)
            .IsRequired();

        builder.Property(l => l.TotalTeams)
            .IsRequired();

        builder.Property(l => l.IsActive)
            .IsRequired();

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        builder.Property(l => l.UpdatedAt);

        // SleeperLeagueId + Season must be unique — same league can exist across seasons
        builder.HasIndex(l => new { l.SleeperLeagueId, l.Season })
            .IsUnique();
    }
}