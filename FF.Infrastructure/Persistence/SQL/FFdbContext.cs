using FF.Domain.Entities;
using FF.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL;

public class FFDbContext(DbContextOptions<FFDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Roster> Rosters => Set<Roster>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<LeagueMembership> LeagueMemberships => Set<LeagueMembership>();
    public DbSet<LeaguePrivacyRule> LeaguePrivacyRules => Set<LeaguePrivacyRule>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Critical — must call base for Identity tables
        builder.ApplyConfigurationsFromAssembly(typeof(FFDbContext).Assembly);

        builder.Entity<LeagueMembership>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.LeagueId, e.Season }).IsUnique();
            entity.HasIndex(e => e.SleeperUserId);
        });
    }
}