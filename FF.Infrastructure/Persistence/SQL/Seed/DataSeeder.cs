using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FF.Infrastructure.Persistence.SQL.Seed;

public static class DataSeeder
{
    public static async Task SeedAsync(FFDbContext context)
    {
        await SeedLeaguesAsync(context);
    }

    // Called separately from DatabaseInitialiser with full service provider
    public static async Task SeedIdentityAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }
        if (!await roleManager.RoleExistsAsync("User"))
        {
            await roleManager.CreateAsync(new IdentityRole("User"));
        }
    }

    private static async Task SeedLeaguesAsync(FFDbContext context)
    {
        if (await context.Leagues.AnyAsync()) return;

        var devLeague = FF.Domain.Entities.League.Create(
            name: "Dev Test League",
            sleeperLeagueId: "dev-001",
            season: 2025,
            totalTeams: 12);

        context.Leagues.Add(devLeague);
        await context.SaveChangesAsync();
    }
}