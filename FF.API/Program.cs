using FF.API.Middleware;
using FF.Application;
using FF.Infrastructure;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

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
            policy.WithOrigins("https://localhost:64233", "http://localhost:64234")
                  .AllowAnyMethod()
                  .AllowAnyHeader());
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