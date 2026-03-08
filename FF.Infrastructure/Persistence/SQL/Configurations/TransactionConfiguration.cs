// FF.Infrastructure/Persistence/SQL/Configurations/TransactionConfiguration.cs

using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FF.Infrastructure.Persistence.SQL.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.SleeperTransactionId)
            .IsRequired()
            .HasMaxLength(100);

        // Unique index — prevents duplicate imports (idempotency guarantee)
        builder.HasIndex(t => t.SleeperTransactionId)
            .IsUnique()
            .HasFilter("[SleeperTransactionId] IS NOT NULL");

        builder.Property(t => t.Type)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Week);
        builder.Property(t => t.TransactionDate);

        // Store as nvarchar(max) — JSON dictionaries of variable size
        builder.Property(t => t.AddsJson).HasColumnType("nvarchar(max)");
        builder.Property(t => t.DropsJson).HasColumnType("nvarchar(max)");

        // FK to League — restrict delete (don't cascade delete transactions)
        builder.HasOne(t => t.League)
            .WithMany()
            .HasForeignKey(t => t.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("Transactions");
    }
}
