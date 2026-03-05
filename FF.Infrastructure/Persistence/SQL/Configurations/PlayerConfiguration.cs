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
            .ValueGeneratedNever(); // We set Guid in domain, not DB

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(100);

        // FullName is a computed C# property — not mapped to a column
        builder.Ignore(p => p.FullName);

        builder.Property(p => p.Position)
            .IsRequired()
            .HasConversion<string>()  // Store as "QB", "RB" etc — readable in DB
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

        builder.Property(p => p.YearsExperience);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        // Indexes — we'll query by SleeperPlayerId constantly
        builder.HasIndex(p => p.SleeperPlayerId)
            .IsUnique()
            .HasFilter("[SleeperPlayerId] IS NOT NULL"); // Filtered — allows multiple nulls

        builder.HasIndex(p => p.Position);
        builder.HasIndex(p => p.Status);
    }
}