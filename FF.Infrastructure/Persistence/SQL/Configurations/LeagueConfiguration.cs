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

        // SleeperLeagueId + Season must be unique
        builder.HasIndex(l => new { l.SleeperLeagueId, l.Season })
            .IsUnique();

        builder.Property(l => l.DraftRounds)
            .IsRequired()
            .HasDefaultValue(4);

        builder.Property(l => l.PickYearsOut)
            .IsRequired()
            .HasDefaultValue(3);

        builder.Property(l => l.CanTradePicks)
            .IsRequired()
            .HasDefaultValue(false);

        // Sleeper roster_positions stored as comma-separated string
        // e.g. "QB,RB,RB,WR,WR,TE,FLEX,SUPER_FLEX,BN,BN,BN,BN"
        // Nullable — null means not yet synced from Sleeper
        builder.Property(l => l.RosterPositions)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(l => l.Avatar)
            .HasMaxLength(100)
            .IsRequired(false);
    }
}