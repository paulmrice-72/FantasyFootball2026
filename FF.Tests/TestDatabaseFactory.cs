// FF.Tests/Infrastructure/TestDatabaseFactory.cs
// 
// Provides a shared EF Core InMemory DbContext for all test classes.
// This is what allows CI to run tests without a live SQL Server connection.
// The build.yml sets ASPNETCORE_ENVIRONMENT=Testing which triggers this path.

using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;

namespace FF.Tests.Infrastructure;

/// <summary>
/// Creates a fresh InMemory FFDbContext for each test.
/// Use this in all test classes that need database access.
/// </summary>
public static class TestDatabaseFactory
{
    /// <summary>
    /// Creates a new isolated InMemory DbContext instance.
    /// Each call gets its own database name, ensuring full test isolation.
    /// </summary>
    public static FFDbContext CreateInMemoryContext(string? databaseName = null)
    {
        var dbName = databaseName ?? $"FFAnalytics_Test_{Guid.NewGuid()}";

        var options = new DbContextOptionsBuilder<FFDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .EnableSensitiveDataLogging()   // helpful for debugging test failures
            .EnableDetailedErrors()
            .Options;

        var context = new FFDbContext(options);

        // Ensure schema is created (InMemory doesn't need migrations)
        context.Database.EnsureCreated();

        return context;
    }

    /// <summary>
    /// Creates a pre-seeded InMemory context with standard test data.
    /// Use this when you need known entities in the database for your test.
    /// </summary>
    public static FFDbContext CreateSeededInMemoryContext(string? databaseName = null)
    {
        var context = CreateInMemoryContext(databaseName);
        SeedTestData(context);
        return context;
    }

    private static void SeedTestData(FFDbContext context)
    {
        // Standard test league
        var league = FF.Domain.Entities.League.Create(
            name: "CI Test League",
            sleeperLeagueId: "ci-test-league-001",
            season: 2024,
            totalTeams: 12);
        context.Leagues.Add(league);

        // Standard test players - one per key position
        var players = new[]
        {
            FF.Domain.Entities.Player.Create("Patrick", "Mahomes", FF.Domain.Enums.Position.QB, "KC", "ci-player-qb-001"),
            FF.Domain.Entities.Player.Create("Christian", "McCaffrey", FF.Domain.Enums.Position.RB, "SF", "ci-player-rb-001"),
            FF.Domain.Entities.Player.Create("Tyreek", "Hill", FF.Domain.Enums.Position.WR, "MIA", "ci-player-wr-001"),
            FF.Domain.Entities.Player.Create("Travis", "Kelce", FF.Domain.Enums.Position.TE, "KC", "ci-player-te-001"),
        };
        context.Players.AddRange(players);

        // Standard test roster
        var roster = FF.Domain.Entities.Roster.Create(
            leagueId: league.Id,
            ownerName: "CI Test Owner",
            teamName: "CI Test Team",
            sleeperRosterId: "ci-roster-001");
        context.Rosters.Add(roster);

        context.SaveChanges();
    }
}
