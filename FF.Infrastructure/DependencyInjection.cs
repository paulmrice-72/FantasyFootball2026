using FF.Application.Interfaces.Jobs;
using FF.Application.Interfaces.Persistence;
using FF.Infrastructure.Jobs;
using FF.Infrastructure.Persistence.Mongo;
using FF.Infrastructure.Persistence.SQL;
using FF.Infrastructure.Persistence.SQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Hangfire;

namespace FF.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        services.AddDbContext<FFDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

        services.AddSingleton<MongoDbContext>();

        // Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<ILeagueRepository, LeagueRepository>();
        services.AddScoped<IRosterRepository, RosterRepository>();

        // Health Checks
        services.AddHealthChecks()
            .AddDbContextCheck<FFDbContext>("sql-server")
            .AddCheck<MongoHealthCheck>("mongodb");

        // Hangfire
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(configuration.GetConnectionString("DefaultConnection")));

        services.AddHangfireServer();
        services.AddScoped<IBackgroundJobService, HangfireBackgroundJobService>();
        services.AddScoped<SystemHealthCheckJob>();

        return services;
    }
}