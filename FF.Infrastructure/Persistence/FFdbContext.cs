using FF.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence;

public class FFDbContext : DbContext
{
    public FFDbContext(DbContextOptions<FFDbContext> options) : base(options) { }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Roster> Rosters => Set<Roster>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Auto-discovers all IEntityTypeConfiguration<T> classes in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FFDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}