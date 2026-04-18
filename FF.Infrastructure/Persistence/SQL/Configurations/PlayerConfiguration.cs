// FF.Infrastructure/Persistence/SQL/Configurations/PlayerConfiguration.cs
using FF.Domain.Entities;
using FF.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FF.Infrastructure.Persistence.SQL.Configurations;

public class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Ignore(p => p.FullName);

        builder.Property(p => p.Position)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.NflTeam)
            .HasMaxLength(50);

        builder.Property(p => p.JerseyNumber);

        builder.Property(p => p.SleeperPlayerId)
            .HasMaxLength(50);

        builder.Property(p => p.Age);
        builder.Property(p => p.BirthDate);
        builder.Ignore(p => p.ComputedAge);   // computed, not stored

        builder.Property(p => p.YearsExperience);

        // ── E10 Dynasty Draft ─────────────────────────────────────────
        builder.Property(p => p.DraftRound);
        builder.Property(p => p.DraftPick);
        builder.Property(p => p.CollegeTeam)
            .HasMaxLength(100);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        builder.HasIndex(p => p.SleeperPlayerId)
            .IsUnique()
            .HasFilter("\"SleeperPlayerId\" IS NOT NULL");

        builder.HasIndex(p => p.Position);
        builder.HasIndex(p => p.Status);

        // ── E10 — query rookies by YearsExperience ────────────────────
        builder.HasIndex(p => p.YearsExperience);
    }
}