// FF.API/Controllers/AdminController.cs
using FF.Application.Features.Admin.Commands.SetPlatformSettings;
using FF.Application.Features.Admin.Queries.GetPlatformSettings;
using FF.Application.Features.Calibration.Commands;
using FF.Application.Features.DraftTools.Commands.ImportFantasyProsDynastyRankings;
using FF.Application.Features.DraftTools.Commands.SyncCombineData;
using FF.Application.Features.Simulations.Commands.SeedSeasonAverageSims;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Enums;
using FF.Infrastructure.Identity;
using FF.Infrastructure.Jobs;
using FF.Infrastructure.Services;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static FF.API.Controllers.DraftToolsController;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(
    UserManager<ApplicationUser> userManager,
    IAppSettingsRepository appSettingsRepo,
    IPlatformSettingsRepository platformSettingsRepo,
    ILogger<AdminController> logger) : ControllerBase
{
    [HttpGet("combine-debug")]
    public async Task<IActionResult> CombineDebug(
        [FromServices] IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("NflverseClient");
        var csv = await http.GetStringAsync(
            "https://github.com/nflverse/nflverse-data/releases/download/combine/combine.csv", ct);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var with2026Drills = lines.Skip(1)
            .Where(l => l.StartsWith("2026") && l.Split(',').Length > 12
                        && !string.IsNullOrWhiteSpace(l.Split(',')[12]))
            .Take(5).ToList();

        var countBySeason = lines.Skip(1)
            .GroupBy(l => l.Split(',')[0])
            .Select(g => new { season = g.Key, count = g.Count() })
            .OrderByDescending(x => x.season)
            .Take(5).ToList();

        return Ok(new { with2026Drills, countBySeason });
    }

    [HttpGet("platform-settings")]
    public async Task<IActionResult> GetPlatformSettings()
    {
        var settings = await platformSettingsRepo.GetAsync();
        return Ok(new
        {
            settings.RegistrationsEnabled,
            settings.AiJobsEnabled,
            settings.UpdatedAt,
            settings.UpdatedBy
        });
    }

    [HttpPut("platform-settings")]
    public async Task<IActionResult> SetPlatformSettings([FromBody] SetPlatformSettingsRequest request)
    {
        var settings = await platformSettingsRepo.GetAsync();
        settings.RegistrationsEnabled = request.RegistrationsEnabled;
        settings.AiJobsEnabled = request.AiJobsEnabled;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedBy = User.Identity?.Name ?? "admin";
        await platformSettingsRepo.SaveAsync(settings);
        return NoContent();
    }

    public record SetPlatformSettingsRequest(bool RegistrationsEnabled, bool AiJobsEnabled);

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        var users = await userManager.Users.ToListAsync(ct);
        var result = new List<object>();
        foreach (var u in users)
        {
            var roles = await userManager.GetRolesAsync(u);
            result.Add(new
            {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.SleeperUserId,
                IsSleeperLinked = !string.IsNullOrEmpty(u.SleeperUserId),
                Roles = roles
            });
        }
        return Ok(result);
    }

    [HttpPost("users/{email}/make-admin")]
    public async Task<IActionResult> MakeAdmin(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return NotFound($"User {email} not found.");
        if (await userManager.IsInRoleAsync(user, "Admin")) return Ok($"{email} is already an Admin.");
        await userManager.AddToRoleAsync(user, "Admin");
        return Ok($"{email} is now an Admin.");
    }

    [HttpPost("users/{email}/remove-admin")]
    public async Task<IActionResult> RemoveAdmin(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return NotFound($"User {email} not found.");
        await userManager.RemoveFromRoleAsync(user, "Admin");
        return Ok($"Admin role removed from {email}.");
    }

    [HttpGet("nfl-context")]
    public async Task<IActionResult> GetNflContext()
    {
        var settings = await appSettingsRepo.GetAsync();
        var calendarSeason = NflContextService.CalcSeason(DateTime.UtcNow);
        var calendarWeek = NflContextService.CalcWeek(DateTime.UtcNow, calendarSeason);
        return Ok(new
        {
            CalendarSeason = calendarSeason,
            CalendarWeek = calendarWeek,
            OverrideSeason = settings.SimulationSeasonOverride,
            OverrideWeek = settings.SimulationWeekOverride,
            IsOverrideActive = settings.SimulationSeasonOverride.HasValue || settings.SimulationWeekOverride.HasValue,
            UpdatedAt = settings.UpdatedAt,
            UpdatedBy = settings.UpdatedBy
        });
    }

    [HttpPost("nfl-context")]
    public async Task<IActionResult> SetNflContext([FromBody] NflContextOverrideRequest request)
    {
        if (request.Season.HasValue && (request.Season < 2020 || request.Season > 2030))
            return BadRequest("Season must be between 2020 and 2030.");
        if (request.Week.HasValue && (request.Week < 0 || request.Week > 18))
            return BadRequest("Week must be between 0 (preseason) and 18.");

        var settings = await appSettingsRepo.GetAsync();
        settings.SimulationSeasonOverride = request.Season;
        settings.SimulationWeekOverride = request.Week;
        settings.UpdatedBy = User.Identity?.Name ?? "admin";
        await appSettingsRepo.UpsertAsync(settings);

        return Ok(new
        {
            Message = request.Season.HasValue || request.Week.HasValue
                ? $"Override set: Season {request.Season}, Week {request.Week}"
                : "Override cleared — using calendar values.",
            OverrideSeason = settings.SimulationSeasonOverride,
            OverrideWeek = settings.SimulationWeekOverride
        });
    }

    [HttpDelete("nfl-context")]
    public async Task<IActionResult> ClearNflContext()
    {
        var settings = await appSettingsRepo.GetAsync();
        settings.SimulationSeasonOverride = null;
        settings.SimulationWeekOverride = null;
        settings.UpdatedBy = User.Identity?.Name ?? "admin";
        await appSettingsRepo.UpsertAsync(settings);
        return Ok("Simulation override cleared.");
    }

    [HttpPost("jobs/run-career-sims")]
    public IActionResult RunCareerSims([FromBody] RunJobRequest request)
    {
        logger.LogInformation("Admin enqueuing career sims — season {Season}", request.Season);
        var jobId = BackgroundJob.Enqueue<RecalculateDynastyValuationsJob>(
            job => job.RunAsync(request.Season, CancellationToken.None));
        return Accepted(new
        {
            Message = $"Dynasty pipeline queued — job {jobId}. Monitor at /hangfire.",
            JobId = jobId
        });
    }

    /// <summary>
    /// Runs DFV calculation inline (not queued).
    /// ScoringFormat defaults to Superflex — pass a different value for standard leagues.
    /// Valid values: Standard, HalfPpr, FullPpr, Superflex, SuperflexFullPpr
    /// </summary>
    [HttpPost("jobs/run-dfv")]
    public async Task<IActionResult> RunDfv(
        [FromBody] RunDfvRequest request,
        [FromServices] IDfvCalculationService dfvService,
        [FromServices] IDynastyValuationRepository valuationRepository,
        CancellationToken ct)
    {
        // Parse scoring format — default to Superflex if missing or invalid
        var scoringFormat = ScoringFormat.Superflex;
        if (!string.IsNullOrWhiteSpace(request.ScoringFormat)
            && Enum.TryParse<ScoringFormat>(request.ScoringFormat, ignoreCase: true, out var parsed))
        {
            scoringFormat = parsed;
        }

        logger.LogInformation(
            "Admin triggered DFV calculation — season {Season}, format {Format}",
            request.Season, scoringFormat);

        var results = await dfvService.CalculateAllAsync(request.Season, scoringFormat, ct);
        await valuationRepository.UpsertBatchAsync(results, ct);

        return Ok(new
        {
            Message = "DFV calculation complete.",
            Count = results.Count,
            ScoringFormat = scoringFormat.ToString()
        });
    }

    [HttpPost("jobs/run-stats-sync")]
    public async Task<IActionResult> RunStatsSync(
        [FromBody] RunJobRequest request,
        [FromServices] HistoricalStatsSyncJob statsSyncJob,
        CancellationToken ct)
    {
        logger.LogInformation("Admin triggered stats sync — season {Season}", request.Season);
        await statsSyncJob.SyncCurrentSeasonAsync(request.Season);
        return Ok(new { Message = $"Stats sync complete for season {request.Season}." });
    }

    [HttpPost("jobs/run-combine-sync")]
    public async Task<IActionResult> RunCombineSync(
        [FromQuery] int season,
        [FromServices] SyncCombineDataCommandHandler combineSync,
        CancellationToken ct)
    {
        logger.LogInformation("Admin triggered combine sync — season {Season}", season);
        var result = await combineSync.Handle(
            new FF.Application.Features.DraftTools.Commands.SyncCombineData.SyncCombineDataCommand(season), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error.Message);
    }

    [HttpGet("nfl-context/public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetNflContextPublic()
    {
        var settings = await appSettingsRepo.GetAsync();
        var calendarSeason = NflContextService.CalcSeason(DateTime.UtcNow);
        var calendarWeek = NflContextService.CalcWeek(DateTime.UtcNow, calendarSeason);
        var activeSeason = settings.SimulationSeasonOverride ?? calendarSeason;
        var activeWeek = settings.SimulationWeekOverride ?? calendarWeek;
        return Ok(new { ActiveSeason = activeSeason, ActiveWeek = activeWeek });
    }

    [HttpPost("jobs/run-projections")]
    public async Task<IActionResult> RunProjections(
        [FromBody] RunJobRequest request,
        [FromServices] ProjectionRefreshJob projectionJob,
        CancellationToken ct)
    {
        logger.LogInformation("Admin triggered projection refresh — season {Season}", request.Season);
        await projectionJob.RunAsync("admin-trigger", ct);
        return Ok(new { Message = $"Projection calculation and simulation complete for season {request.Season}." });
    }

    [HttpPost("jobs/run-snap-count-sync")]
    public async Task<IActionResult> RunSnapCountSync(
        [FromServices] SnapCountSyncJob snapCountJob,
        CancellationToken ct)
    {
        logger.LogInformation("Admin triggered snap count sync");
        await snapCountJob.RunAsync();
        return Ok(new { Message = "Snap count sync complete." });
    }

    [HttpPost("jobs/run-article-generation")]
    public async Task<IActionResult> RunArticleGeneration(
        [FromServices] ArticleGenerationJob articleJob,
        CancellationToken ct)
    {
        logger.LogInformation("Admin triggered article generation");
        await articleJob.RunAsync(ct);
        return Ok(new { Message = "Article generation complete." });
    }

    [HttpPost("sync-ffc-adp")]
    public IActionResult TriggerFfcAdpSync()
    {
        BackgroundJob.Enqueue<SyncRedraftAdpJob>(job => job.RunAsync(CancellationToken.None));
        return Ok(new { message = "FFC ADP sync job enqueued." });
    }

    [HttpPost("jobs/seed-season-averages")]
    public async Task<IActionResult> SeedSeasonAverages(
        [FromQuery] int season,
        [FromServices] IMediator mediator,
        CancellationToken ct)
    {
        logger.LogInformation("Admin triggered season-average sim seed for season {Season}", season);
        if (season < 2020 || season > DateTime.UtcNow.Year)
            return BadRequest($"Season must be between 2020 and {DateTime.UtcNow.Year}.");

        var result = await mediator.Send(new SeedSeasonAverageSimsCommand(season), ct);
        return Ok(new
        {
            Message = $"Season-average sim seed complete for {season}.",
            result.Seeded,
            result.Skipped,
            result.Unmatched
        });
    }

    [HttpPost("jobs/run-calibration")]
    public async Task<IActionResult> RunCalibration(
    [FromBody] RunCalibrationRequest request,
    [FromServices] IMediator mediator,
    CancellationToken ct)
    {
        logger.LogInformation(
            "Admin triggered calibration harness — season {Season}, format {Format}",
            request.Season, request.ScoringFormat);

        var result = await mediator.Send(
            new RunCalibrationCommand(request.Season, request.ScoringFormat ?? "Superflex"), ct);

        return Ok(new
        {
            result.SpearmanRho,
            result.AvgAbsDelta,
            result.Top10Overlap,
            result.PlayerCount,
            Top20Snapshot = result.Top20Snapshot
        });
    }

    [HttpGet("calibration/latest")]
    public async Task<IActionResult> GetLatestCalibration(
        [FromServices] ICalibrationResultRepository calibrationRepo,
        CancellationToken ct)
    {
        var latest = await calibrationRepo.GetLatestAsync(ct);
        if (latest is null)
            return NotFound("No calibration runs found. Run calibration from the Admin Imports page.");

        return Ok(latest);
    }

    [HttpGet("calibration/history")]
    public async Task<IActionResult> GetCalibrationHistory(
        [FromServices] ICalibrationResultRepository calibrationRepo,
        CancellationToken ct,
        [FromQuery] int count = 10)
    {
        var history = await calibrationRepo.GetRecentAsync(count, ct);
        return Ok(history);
    }

    [HttpPost("import/fantasypros-dynasty")]
    public async Task<IActionResult> ImportFantasyProsDynastyRankings(
    [FromBody] AdminImportFantasyProsRequest request,
    [FromServices] IMediator mediator,
    CancellationToken ct)
    {
        logger.LogInformation(
            "Admin triggered FP Dynasty Rankings import — season {Season}", request.Season);

        var result = await mediator.Send(
            new ImportFantasyProsDynastyRankingsCommand(request.CsvContent, request.Season), ct);

        return result.IsSuccess
            ? Ok(new { result.Value.Imported, result.Value.Unmatched, result.Value.Season })
            : BadRequest(result.Error);
    }

    [HttpPost("import/seed-season-averages-csv")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> SeedSeasonAveragesCsv(
      [FromBody] SeedSeasonAveragesCsvRequest request,
      [FromServices] IMediator mediator,
      CancellationToken ct)
    {
        if (request.Season < 2020 || request.Season > DateTime.UtcNow.Year + 1)
            return BadRequest($"Season must be between 2020 and {DateTime.UtcNow.Year + 1}.");

        if (string.IsNullOrWhiteSpace(request.CsvContent))
            return BadRequest("CsvContent is required.");

        logger.LogInformation(
            "Admin triggered season-average sim seed via CSV upload — season {Season}", request.Season);

        var result = await mediator.Send(
            new SeedSeasonAverageSimsCommand(request.Season, request.CsvContent), ct);

        return Ok(new
        {
            Message = $"Season-average sim seed complete for {request.Season}.",
            result.Seeded,
            result.Skipped,
            result.Unmatched
        });
    }

    public record SeedSeasonAveragesCsvRequest(string CsvContent, int Season);
    // ── Request records ─────────────────────────────────────────────────────
    public record RunJobRequest(int Season);

    /// <summary>
    /// DFV-specific request — extends RunJobRequest with optional ScoringFormat.
    /// ScoringFormat string is parsed to the enum server-side; invalid values default to Superflex.
    /// </summary>
    public record RunDfvRequest(int Season, string? ScoringFormat = null);

    public record NflContextOverrideRequest(int? Season, int? Week);
    public record RunCalibrationRequest(int Season, string? ScoringFormat);
    public record AdminImportFantasyProsRequest(string CsvContent, int Season);
}