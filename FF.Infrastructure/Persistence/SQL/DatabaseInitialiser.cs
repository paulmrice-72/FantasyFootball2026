using FF.Infrastructure.Persistence.SQL.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FF.Infrastructure.Persistence.SQL;

public static class DatabaseInitialiser
{
    public static async Task InitialiseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FFDbContext>();
        try
        {
            await context.Database.MigrateAsync();
            await DataSeeder.SeedAsync(context);
            Log.Information("Database migration and seeding completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine("=== DatabaseInitialiser FAILED ===");
            Console.WriteLine(ex.GetType().FullName);
            Console.WriteLine(ex.Message);
            if (ex.InnerException != null)
            {
                Console.WriteLine("Inner: " + ex.InnerException.Message);
            }
            Console.WriteLine(ex.StackTrace);
            Log.Error(ex, "An error occurred during database migration or seeding");
            throw;
        }
    }
}