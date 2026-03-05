using FF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Seed;

public static class DataSeeder
{
    public static async Task SeedAsync(FFDbContext context)
    {
        await SeedLeaguesAsync(context);
    }

    private static async Task SeedLeaguesAsync(FFDbContext context)
    {
        // Only seed if no data exists — idempotent
        if (await context.Leagues.AnyAsync()) return;

        // Reference league for development/testing
        var devLeague = FF.Domain.Entities.League.Create(
            name: "Dev Test League",
            sleeperLeagueId: "dev-001",
            season: 2025,
            totalTeams: 12);

        context.Leagues.Add(devLeague);
        await context.SaveChangesAsync();
    }
}