using FF.Application;
using FF.Application.Common.Settings;
using FF.Application.Identity.Interfaces;
using FF.Application.Interfaces.Auth;
using FF.Application.Interfaces.Jobs;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Application.Interfaces.Services.Usage;
using FF.Domain.Documents;
using FF.Infrastructure.ExternalApis.CsvImport;
using FF.Infrastructure.ExternalApis.CsvImport.Parsers;
using FF.Infrastructure.ExternalApis.Nflverse;
using FF.Infrastructure.ExternalApis.Sleeper;
using FF.Infrastructure.ExternalAPIs;
using FF.Infrastructure.Identity;
using FF.Infrastructure.Jobs;
using FF.Infrastructure.Persistence.Mongo;
using FF.Infrastructure.Persistence.Mongo.Repositories;
using FF.Infrastructure.Persistence.SQL;
using FF.Infrastructure.Persistence.SQL.Repositories;
using FF.Infrastructure.Services;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;

namespace FF.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HistoricalDataSettings>(configuration.GetSection(HistoricalDataSettings.SectionName));

        RegisterBsonClassMaps();

        System.Diagnostics.Debug.WriteLine("AddInfrastructure starting...");
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
        services.AddScoped<IPlayerGameLogRepository, PlayerGameLogRepository>();
        services.AddScoped<IUsageMetricsRepository, UsageMetricsRepository>();
        services.AddScoped<ISnapCountRepository, SnapCountRepository>();
        services.AddScoped<IUsageMetricsService, UsageMetricsService>();
        services.AddScoped<UsageMetricsAggregationJob>();
        services.AddScoped<ILeagueMembershipRepository, LeagueMembershipRepository>();

        // Add named HttpClient for nflverse — GitHub redirects require following redirects
        services.AddHttpClient<NflverseDownloadService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2); // CSV can be large
            client.DefaultRequestHeaders.Add(
                "User-Agent", "FantasyCombine.AI/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true // GitHub releases use redirects
        });

        services.AddScoped<INflverseDownloadService, NflverseDownloadService>();

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
        services.AddScoped<ISleeperLeagueImportService, SleeperLeagueImportService>();
        services.AddScoped<ISleeperPlayerSyncService, SleeperPlayerSyncService>();
        services.AddScoped<IPlayerIdResolutionService, PlayerIdResolutionService>();
        services.AddScoped<SystemHealthCheckJob>();
        services.AddSleeperApiClient();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(InfrastructureAssemblyMarker).Assembly);
        });

        // Identity
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = false;
        })
        .AddEntityFrameworkStores<FFDbContext>()
        .AddDefaultTokenProviders();

        // JWT Settings
        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

        // Auth Service
        services.AddScoped<IAuthService, AuthService>();

        // CSV Import
        services.AddScoped<NflfastrCsvParser>();
        services.AddScoped<PfrCsvParser>();
        services.AddScoped<PfrValidationService>();
        services.AddScoped<IHistoricalStatsImportService, HistoricalStatsImportService>();
        services.AddScoped<ISnapCountImportService, SnapCountImportService>();
        services.AddScoped<ISnapCountMergeService, SnapCountMergeService>();
        services.AddScoped<SnapCountSyncJob>();
        services.AddScoped<SnapCountCsvParser>();

        // Register Hangfire job classes so DI can resolve them
        services.AddScoped<HistoricalStatsSyncJob>();
        services.AddScoped<SystemHealthCheckJob>();
        services.AddScoped<LeagueSyncJob>();
        services.AddScoped<PlayerSyncJob>();

        // Sleeper Identity
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddHttpClient<ISleeperIdentityService, SleeperIdentityService>(client =>
        {
            client.BaseAddress = new Uri("https://api.sleeper.app");
            client.DefaultRequestHeaders.Add("User-Agent", "FantasyCombine.AI/1.0");
        });

        return services;


    }

    private static void RegisterBsonClassMaps()
    {
        try
        {
            var decimalSerializer = new DecimalSerializer(BsonType.Decimal128);

            if (!BsonClassMap.IsClassMapRegistered(typeof(PlayerUsageMetricsDocument)))
            {
                System.Diagnostics.Debug.WriteLine("Registering PlayerUsageMetricsDocument...");
                BsonClassMap.RegisterClassMap<PlayerUsageMetricsDocument>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    cm.MapIdMember(c => c.Id)
                      .SetIdGenerator(StringObjectIdGenerator.Instance)
                      .SetSerializer(new StringSerializer(BsonType.ObjectId));

                    cm.MapMember(c => c.TargetShare3Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.TargetShare5Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.TargetShareSeason).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.SnapPct3Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.SnapPct5Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.SnapPctSeason).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.AirYardsShare3Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.AirYardsShare5Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.AirYardsShareSeason).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.CarryShare3Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.CarryShare5Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.CarryShareSeason).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.Wopr3Wk).SetSerializer(decimalSerializer);
                    cm.MapMember(c => c.WoprSeason).SetSerializer(decimalSerializer);
                });
                System.Diagnostics.Debug.WriteLine("PlayerUsageMetricsDocument registered OK");
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(PlayerGameLogDocument)))
            {
                BsonClassMap.RegisterClassMap<PlayerGameLogDocument>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    cm.MapIdMember(c => c.Id)
                      .SetIdGenerator(StringObjectIdGenerator.Instance)
                      .SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FAILED PlayerUsageMetricsDocument: {ex.Message}");
            throw;
        }

        try
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(SnapCountDocument)))
            {
                System.Diagnostics.Debug.WriteLine("Registering SnapCountDocument...");
                BsonClassMap.RegisterClassMap<SnapCountDocument>(cm =>
                {
                    cm.AutoMap();
                    cm.MapIdMember(c => c.Id)
                      .SetIdGenerator(StringObjectIdGenerator.Instance)
                      .SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                System.Diagnostics.Debug.WriteLine("SnapCountDocument registered OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FAILED SnapCountDocument: {ex.Message}");
            throw;
        }
    }
}