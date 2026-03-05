using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FF.Infrastructure.Persistence.SQL.Configurations;

public class RosterConfiguration : IEntityTypeConfiguration<Roster>
{
    public void Configure(EntityTypeBuilder<Roster> builder)
    {
        builder.ToTable("Rosters");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.LeagueId)
            .IsRequired();

        builder.Property(r => r.OwnerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.TeamName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.SleeperRosterId)
            .HasMaxLength(50);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt);

        // Foreign key to League
        builder.HasOne(r => r.League)
            .WithMany()
            .HasForeignKey(r => r.LeagueId)
            .OnDelete(DeleteBehavior.Restrict); // Don't cascade delete rosters

        builder.HasIndex(r => r.LeagueId);

        builder.HasIndex(r => r.SleeperRosterId)
            .IsUnique()
            .HasFilter("[SleeperRosterId] IS NOT NULL");
    }
}