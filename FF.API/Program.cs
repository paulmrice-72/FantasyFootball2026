using FF.API.Middleware;
using FF.Application;
using FF.Application.Common.Settings;
using FF.Application.Interfaces.Persistence;
using FF.Application.Stats.Queries.GetHistoricalStatsStatus;
using FF.Infrastructure;
using FF.Infrastructure.Jobs;
using FF.Infrastructure.Persistence.Mongo.Repositories;
using FF.Infrastructure.Persistence.SQL;
using FF.SharedKernel.Common;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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
            ClockSkew = TimeSpan.Zero // No grace period on token expiry
        };
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
        .WriteTo.Seq(context.Configuration["Seq:ServerUrl"]
            ?? "http://192.168.6.17:5341"));

    // ── API SERVICES ──────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // ── CORS (for Blazor WASM) ────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("BlazorWasm", policy =>
        {
            policy.WithOrigins(
                    "https://localhost:64233",
                    "http://localhost:64234",
                    "https://localhost:64235",
                    "http://localhost:64236")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    await DatabaseInitialiser.InitialiseAsync(app.Services);
    
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
    app.UseAuthorization();
    app.MapControllers();

    app.MapControllers();

    // ── HANGFIRE DASHBOARD ────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter()]
        });
    }

    // ── STARTUP TASKS ─────────────────────────────────────
    // DatabaseInitialiser runs migrations + seed on every startup (idempotent)
    await DatabaseInitialiser.InitialiseAsync(app.Services);

    // MongoDB index creation — idempotent, safe to run on every startup
    using (var scope = app.Services.CreateScope())
    {
        var gameLogRepo = scope.ServiceProvider
            .GetRequiredService<IPlayerGameLogRepository>();
        await gameLogRepo.EnsureIndexesAsync();
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
        methodCall: x => x.SyncCurrentSeasonAsync(),
        cronExpression: Cron.Weekly(DayOfWeek.Tuesday, 8),
        options: utcOptions);

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