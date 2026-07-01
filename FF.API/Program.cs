using FF.API.Infrastructure;
using FF.API.Middleware;
using FF.Application;
using FF.Application.Common.Settings;
using FF.Application.Interfaces.Persistence;
using FF.Application.Stats.Queries.GetHistoricalStatsStatus;
using FF.Infrastructure;
using FF.Infrastructure.ExternalServices.OddsAPI;
using FF.Infrastructure.Jobs;
using FF.Infrastructure.Persistence.Mongo.Repositories;
using FF.Infrastructure.Persistence.SQL;
using FF.SharedKernel.Common;
using Hangfire;
using MathNet.Numerics;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Refit;
using Serilog;
using Serilog.Events;
using System.Text;


Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine($"SERILOG: {msg}"));

// ── BOOTSTRAP LOGGER ─────────────────────────────────────
// Captures startup errors before full Serilog is configured
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting FF Analytics API");

    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // JWT Authentication
    var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddCookie("HangfireCookie", options =>
    {
        options.LoginPath = "/api/hangfire/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

    // ── SERILOG ───────────────────────────────────────────
    builder.Host.UseSerilog((context, services, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId()
        .Enrich.WithCorrelationId()
        .WriteTo.Console()
        .WriteTo.Seq(string.IsNullOrWhiteSpace(context.Configuration["Seq:ServerUrl"]) ? "http://localhost:8082" : context.Configuration["Seq:ServerUrl"]!));

    // ── API SERVICES ──────────────────────────────────────
    builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Enter your JWT token. Example: eyJhbGci..."
        });
        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id   = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ── CORS (for Blazor WASM) ────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("BlazorWasm", policy =>
        {
            policy.WithOrigins(
                    "https://localhost:64233",
                    "http://localhost:64234",
                    "https://localhost:64235",
                    "http://localhost:64236",
                    "http://192.168.6.22:64235",   // ← add PMRDEPLOY
                    "http://192.168.6.22:64233",  // ← add PMRDEPLOY")
                    "https://fantasycombineai.com",
                    "https://www.fantasycombineai.com")   
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });

    // Odds API
    builder.Services.Configure<OddsApiSettings>(builder.Configuration.GetSection("OddsApi"));
    builder.Services.AddRefitClient<IOddsApiClient>()
        .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://api.the-odds-api.com"));


    var app = builder.Build();

   
    // ── MIDDLEWARE PIPELINE ───────────────────────────────
    // Order matters — do not rearrange without understanding the implications
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description
                })
            });
            await context.Response.WriteAsync(result);
        }
    });
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseCors("BlazorWasm");
    app.UseAuthentication();
    app.UseAuthorization();

    // ── HANGFIRE LOGIN ────────────────────────────────────
    app.MapPost("/api/hangfire/login", async (HttpContext ctx, IConfiguration config) =>
    {
        var password = ctx.Request.Form["password"].ToString();
        var adminPassword = config["HangfireAdmin:Password"];
        if (password != adminPassword)
            return Results.Redirect("/api/hangfire/login?error=1");

        var claims = new[] {
        new System.Security.Claims.Claim(
            System.Security.Claims.ClaimTypes.Role, "Admin")
    };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "HangfireCookie");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        await ctx.SignInAsync("HangfireCookie", principal);
        return Results.Redirect("/hangfire");
    });

    app.MapGet("/api/hangfire/login", () => Results.Content("""
    <html><body>
    <form method='post' action='/api/hangfire/login'>
        <input type='password' name='password' placeholder='Admin password' />
        <button type='submit'>Login</button>
    </form>
    </body></html>
""", "text/html"));

    // ── HANGFIRE DASHBOARD ────────────────────────────────
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireAuthorizationFilter()]
    });
    app.MapControllers();

    // ── STARTUP TASKS ─────────────────────────────────────
    await DatabaseInitialiser.InitialiseAsync(app.Services);

    // MongoDB index creation — idempotent, safe to run on every startup
    using (var scope = app.Services.CreateScope())
    {
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<IPlayerGameLogRepository>().EnsureIndexesAsync();
        await sp.GetRequiredService<ISnapCountRepository>().EnsureIndexesAsync();
        await sp.GetRequiredService<IDefensiveRankingRepository>().EnsureIndexesAsync();

    }

    // ── RECURRING JOBS ────────────────────────────────────
    // Static Hangfire client --- no scope needed for registration
    var utcOptions = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };

    RecurringJob.AddOrUpdate<SystemHealthCheckJob>(
        recurringJobId: "system-health-check",
        methodCall: job => job.Execute(),
        cronExpression: "*/15 * * * *",
        options: utcOptions);

    RecurringJob.AddOrUpdate<LeagueSyncJob>(
        recurringJobId: "league-sync-weekly",
        methodCall: job => job.SyncAllLeaguesAsync(),
        cronExpression: "0 10 * * 2",
        options: utcOptions);

    RecurringJob.AddOrUpdate<PlayerSyncJob>(
        recurringJobId: "player-sync-weekly",
        methodCall: job => job.SyncPlayersAsync(),
        cronExpression: "0 6 * * 2",
        options: utcOptions);

    RecurringJob.AddOrUpdate<HistoricalStatsSyncJob>(
        recurringJobId: "weekly-stats-sync",
        methodCall: x => x.RunAsync(),
        cronExpression: Cron.Weekly(DayOfWeek.Tuesday, 8),
        options: utcOptions);

    RecurringJob.AddOrUpdate<UsageMetricsAggregationJob>(
        recurringJobId: "usage-metrics-aggregation",
        methodCall: job => job.ExecuteAsync(),
        cronExpression: Cron.Weekly(DayOfWeek.Tuesday, 6),
        options: utcOptions);

    RecurringJob.AddOrUpdate<SnapCountSyncJob>(
        "snap-count-sync",
        job => job.RunAsync(),
        "0 9 * * 2",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    RecurringJob.AddOrUpdate<SimulationJob>(
        "simulation-weekly-wed",
        job => job.RunAsync(),
        "0 6 * * 3",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    RecurringJob.AddOrUpdate<SimulationJob>(
        "simulation-weekly-thu",
        job => job.RunAsync(),
        "0 6 * * 4",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    // Hangfire — add after existing job registrations
    RecurringJob.AddOrUpdate<VegasLineSyncJob>(
        "vegas-line-sync",
        job => job.RunAsync(CancellationToken.None),
        "0 5 * * 3", utcOptions);  // Wednesday 5am UTC — fires before simulation at 6am

    RecurringJob.AddOrUpdate<TnfRefreshJob>(
        "tnf-projection-refresh",
        job => job.RunAsync(CancellationToken.None),
        "0 18 * * 4",   // Thursday 6pm UTC = 1pm ET
        utcOptions);

    RecurringJob.AddOrUpdate<SundayRefreshJob>(
        "sunday-projection-refresh",
        job => job.RunAsync(CancellationToken.None),
        "0 11 * * 0",   // Sunday 11am UTC = 6am ET
        utcOptions);

    RecurringJob.AddOrUpdate<MnfRefreshJob>(
        "mnf-projection-refresh",
        job => job.RunAsync(CancellationToken.None),
        "0 23 * * 1",   // Monday 11pm UTC = 6pm ET
        utcOptions);

    RecurringJob.AddOrUpdate<WarRoomBriefJob>(
        "war-room-brief-sunday",
        job => job.RunAsync(CancellationToken.None, false),  // ← was true
        "0 8 * * 0",
        utcOptions);

    RecurringJob.AddOrUpdate<ArticleGenerationJob>(
        "article-generation-weekly",
        job => job.RunAsync(CancellationToken.None, false),  // ← respects AiJobsEnabled
        "0 10 * * 2",
        utcOptions);

    RecurringJob.AddOrUpdate<EmergenceDetectionJob>(
        "emergence-detection-weekly",
        job => job.RunAsync(2026, 1),
        "0 12 * * 2"); // Tuesdays 12:00 UTC — after usage-metrics-aggregation

    RecurringJob.AddOrUpdate<InjuryAlertSyncJob>(
        "injury-alert-sync-wed",
        job => job.RunAsync(CancellationToken.None),
        "0 14 * * 3",   // Wednesday 2pm UTC — after practice reports release
        utcOptions);

    RecurringJob.AddOrUpdate<InjuryAlertSyncJob>(
        "injury-alert-sync-sun",
        job => job.RunAsync(CancellationToken.None),
        "0 9 * * 0",    // Sunday 9am UTC — final injury report before games
        utcOptions);

    // ── E10 Dynasty Draft — nflverse draft pick sync ──────────────────────
    // Runs daily April 25 – May 15. Disable after DraftRound/DraftPick confirmed populated.
    RecurringJob.AddOrUpdate<NflverseDraftPickSyncJob>(
        "nflverse-draft-pick-sync",
        job => job.RunAsync(2026, CancellationToken.None),
        "0 12 * * *",   // Daily noon UTC — nflverse updates by morning after each draft day
        utcOptions);

    RecurringJob.AddOrUpdate<RecalculateDynastyValuationsJob>(
        "dynasty-recalculate-weekly",
        job => job.RunAsync(2026, CancellationToken.None),
        "0 7 * * 3",  // Wednesday 7:00 UTC — after simulation (6am) and Vegas sync (5am)
        utcOptions);

    RecurringJob.AddOrUpdate<SyncDepthChartsJob>(
        "depth-chart-sync-weekly",
        job => job.RunAsync(2026, CancellationToken.None),
        "0 8 * * 3",  // Wednesday 8:00 UTC — after dynasty pipeline
        utcOptions);

    RecurringJob.AddOrUpdate<SyncRedraftAdpJob>(
        "redraft adp sync",
        job => job.RunAsync(CancellationToken.None),
        Cron.Weekly(DayOfWeek.Tuesday, 9),
        utcOptions);

    BackgroundJob.Enqueue<SeedPickValuesJob>(job => job.SeedAsync());
    //RecurringJob.AddOrUpdate<WaiverSyncJob>(
    //recurringJobId: "waiver-sync",
    //methodCall: job => job.SyncWaiversAsync(),
    //cronExpression: "5 0 * * 3", // 12:05 AM UTC Wednesday = ~8:05 PM ET Tuesday
    //options: new RecurringJobOptions
    //{
    //    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")
    //});

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "FF Analytics API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}