// FF.API/Controllers/AdminController.cs
using FF.Application.Features.Admin.Commands.SetPlatformSettings;
using FF.Application.Features.Admin.Queries.GetPlatformSettings;
using FF.Application.Features.Calibration.Commands;
using FF.Application.Features.DraftTools.Commands.ImportFantasyProsDynastyRankings;
using FF.Application.Features.DraftTools.Commands.SyncCombineData;
using FF.Application.Features.Schedule.Commands;
using FF.Application.Features.Simulations.Commands.SeedSeasonAverageSims;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.ValueObjects;
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

    /// <summary>
    /// Diagnostic view of the season/week resolution.
    ///
    /// <para>
    /// FAN-139/178. This endpoint used to compute <c>override ?? calendar</c>
    /// inline, as did <c>nfl-context/public</c> and the admin page, so the same
    /// rule existed in four places and only one of them learned about the
    /// schedule. The effective values now come from
    /// <see cref="INflContextService"/> — the single owner of that rule — and the
    /// calendar and schedule figures are reported alongside as what they are:
    /// inputs, not answers.
    /// </para>
    /// </summary>
    [HttpGet("nfl-context")]
    public async Task<IActionResult> GetNflContext(
        [FromServices] INflContextService nflContext,
        [FromServices] INflScheduleRepository scheduleRepository,
        CancellationToken ct)
    {
        var settings = await appSettingsRepo.GetAsync();

        var calendarSeason = NflContextService.CalcSeason(DateTime.UtcNow);
        var calendarWeek = NflContextService.CalcWeek(DateTime.UtcNow, calendarSeason);

        var (activeSeason, activeWeek) = await nflContext.GetContextAsync();

        var scheduleWeek = await scheduleRepository.GetCurrentWeekAsync(
            activeSeason, DateTime.UtcNow, ct);

        var weekSource =
            settings.SimulationWeekOverride.HasValue ? "override"
            : scheduleWeek.HasValue ? "schedule"
            : "calendar";

        return Ok(new
        {
            ActiveSeason = activeSeason,
            ActiveWeek = activeWeek,
            WeekSource = weekSource,
            ScheduleWeek = scheduleWeek,
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

    /// <summary>
    /// Queues the full dynasty pipeline: career simulations → breakout detection → DFV.
    ///
    /// <para>
    /// FAN-158: the scoring format is stated explicitly rather than inherited from a
    /// default, and it matches both the weekly schedule in Program.cs and this
    /// controller's <c>run-dfv</c> default. All three have to agree — they write the
    /// same collection, and the last one to run is the board that gets served.
    /// </para>
    /// </summary>
    [HttpPost("jobs/run-career-sims")]
    public IActionResult RunCareerSims([FromBody] RunJobRequest request)
    {
        const ScoringFormat servedFormat = ScoringFormat.Superflex;

        logger.LogInformation(
            "Admin enqueuing career sims — season {Season}, format {Format}",
            request.Season, servedFormat);

        var jobId = BackgroundJob.Enqueue<RecalculateDynastyValuationsJob>(
            job => job.RunAsync(request.Season, servedFormat, CancellationToken.None));
        return Accepted(new
        {
            Message = $"Dynasty pipeline queued — job {jobId}, format {servedFormat}. Monitor at /hangfire.",
            JobId = jobId,
            ScoringFormat = servedFormat.ToString()
        });
    }

    /// <summary>
    /// Builds the positional value curves behind league-aware replacement level —
    /// FAN-153 phase 1.
    ///
    /// <para>
    /// Reads the career simulations already in Mongo and writes one curve per
    /// projected season per position. <b>Nothing reads these yet</b> — no served
    /// value changes when this runs. It exists so the curves can be inspected and
    /// argued with before anything depends on them.
    /// </para>
    ///
    /// <para>
    /// The number to look at in the response is <c>replacementLevelPreview</c>: the
    /// same curves resolved against a standard league and a superflex league. If
    /// the superflex QB level is not markedly lower than the standard one, the
    /// curves are not carrying the scarcity signal and phase 2 has nothing to stand
    /// on.
    /// </para>
    /// </summary>
    [HttpPost("jobs/build-value-curves")]
    public async Task<IActionResult> BuildValueCurves(
        [FromBody] BuildValueCurvesRequest request,
        [FromServices] ICareerSimulationRepository careerSimRepository,
        [FromServices] IPositionalValueCurveRepository curveRepository,
        CancellationToken ct)
    {
        var scoringFormat = ScoringFormat.Superflex;
        if (!string.IsNullOrWhiteSpace(request.ScoringFormat)
            && Enum.TryParse<ScoringFormat>(request.ScoringFormat, ignoreCase: true, out var parsed))
        {
            scoringFormat = parsed;
        }

        var depth = request.Depth ?? PositionalValueCurveBuilder.DefaultDepth;
        var teamCount = request.TeamCount ?? 12;

        logger.LogInformation(
            "Admin triggered value-curve build — season {Season}, format {Format}, depth {Depth}",
            request.Season, scoringFormat, depth);

        var sims = await careerSimRepository.GetAllBySeasonAsync(request.Season, ct);
        if (sims.Count == 0)
        {
            return BadRequest(new
            {
                Message = $"No career simulations found for season {request.Season}. "
                        + "Run jobs/run-career-sims first — a curve built from an empty pool "
                        + "would be a fabricated replacement level of zero."
            });
        }

        var curves = PositionalValueCurveBuilder.Build(sims, depth);
        var computedAt = DateTime.UtcNow;

        // FAN-153 phase 2. Stored under the scoring half of the format only, not
        // the whole enum. ScoringFormat pairs a scoring rule with a roster shape,
        // and only the scoring rule can change a distribution of projected points
        // — Superflex IS Half-PPR scoring. Filing a curve under the paired name
        // would store the same numbers twice and make a Half-PPR read miss a
        // Superflex build, which is what it did on the first run of phase 2.
        var formatName = ValueOverReplacementCalculator.CurveScoringKey(scoringFormat);

        var documents = curves.Select(c => new PositionalValueCurveDocument
        {
            Id = $"{request.Season}:{formatName}:{c.Year}:{c.Position}",
            Season = request.Season,
            ScoringFormat = formatName,
            Year = c.Year,
            Position = c.Position,
            DescendingSeasonValues = [.. c.DescendingSeasonValues],
            PoolSize = c.PoolSize,
            Depth = c.DescendingSeasonValues.Count,
            ComputedAt = computedAt
        }).ToList();

        await curveRepository.UpsertBatchAsync(documents, ct);

        foreach (var c in curves)
        {
            logger.LogInformation(
                "FAN-153 curve: {Year} {Position} — pool {PoolSize}, stored {Stored}, "
                + "top {Top:F1}, at 12 {At12:F1}, at 24 {At24:F1}, at 36 {At36:F1}",
                c.Year, c.Position, c.PoolSize, c.DescendingSeasonValues.Count,
                ValueAt(c.DescendingSeasonValues, 0),
                ValueAt(c.DescendingSeasonValues, 12),
                ValueAt(c.DescendingSeasonValues, 24),
                ValueAt(c.DescendingSeasonValues, 36));
        }

        // The whole point of phase 1, in one comparison: the same curves read by
        // two different leagues. A SUPER_FLEX slot makes quarterbacks eligible for
        // a flex, which pushes the QB cutoff roughly a full round deeper and drops
        // the QB baseline — which is the entire reason elite QBs cost more there.
        var firstYear = curves.Count > 0 ? curves.Min(c => c.Year) : request.Season;
        var preview = new
        {
            Year = firstYear,
            TeamCount = teamCount,
            Standard = ResolvePreview(curves, firstYear, RosterConfiguration.Standard, teamCount),
            Superflex = ResolvePreview(curves, firstYear, RosterConfiguration.Superflex, teamCount)
        };

        logger.LogInformation(
            "FAN-153 replacement-level preview, year {Year} at {Teams} teams — standard [{Standard}], superflex [{Superflex}]",
            preview.Year, preview.TeamCount,
            string.Join(", ", preview.Standard.Select(kv => $"{kv.Key} {kv.Value}")),
            string.Join(", ", preview.Superflex.Select(kv => $"{kv.Key} {kv.Value}")));

        return Ok(new
        {
            Message = "Positional value curves built. Nothing reads them yet — no served value changed.",
            Season = request.Season,
            ScoringFormat = formatName,
            CurvesWritten = documents.Count,
            SimulationsRead = sims.Count,
            Depth = depth,
            ReplacementLevelPreview = preview
        });

        static double ValueAt(IReadOnlyList<double> values, int index)
            => index < values.Count ? values[index] : double.NaN;

        static Dictionary<string, string> ResolvePreview(
            IReadOnlyList<PositionalValueCurve> allCurves,
            int year,
            RosterConfiguration config,
            int teams)
        {
            var forYear = allCurves.Where(c => c.Year == year).ToList();

            var descending = forYear.ToDictionary(
                c => c.Position,
                c => (IReadOnlyList<decimal>)c.DescendingSeasonValues.Select(v => (decimal)v).ToList(),
                StringComparer.OrdinalIgnoreCase);

            var poolSizes = forYear.ToDictionary(
                c => c.Position, c => c.PoolSize, StringComparer.OrdinalIgnoreCase);

            var resolved = ReplacementLevelService.ResolveFromCurves(
                descending, poolSizes, config, teams);

            return resolved.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.BeyondStoredDepth
                    ? $"cutoff {kv.Value.StartersAbsorbed} beyond stored depth"
                    : $"{kv.Value.StructuralLevel:F1} at cutoff {kv.Value.StartersAbsorbed}"
                      + (kv.Value.PoolExhausted ? " (pool exhausted)" : ""));
        }
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
            "Admin triggered DFV calculation — season {Season}, format {Format}, disableVor {DisableVor}",
            request.Season, scoringFormat, request.DisableVor);

        var results = await dfvService.CalculateAllAsync(
            request.Season, scoringFormat, request.DisableVor, ct);
        await valuationRepository.UpsertBatchAsync(results, ct);

        return Ok(new
        {
            Message = request.DisableVor
                ? "DFV calculation complete — CONTROL RUN, value over replacement disabled. "
                  + "The served board is now a measurement state; re-run without disableVor to restore it."
                : "DFV calculation complete.",
            Count = results.Count,
            ScoringFormat = scoringFormat.ToString(),
            DisableVor = request.DisableVor
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

    /// <summary>
    /// The season/week the front end runs on — NflContextClientService and every
    /// page that asks it, including My Matchup, read this.
    ///
    /// FAN-139/178: it resolved <c>override ?? calendar</c> inline, so clearing
    /// the override handed the whole site the calendar heuristic and the imported
    /// schedule was never consulted. It now delegates.
    /// </summary>
    [HttpGet("nfl-context/public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetNflContextPublic(
        [FromServices] INflContextService nflContext)
    {
        var (activeSeason, activeWeek) = await nflContext.GetContextAsync();
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

    /// <summary>
    /// Runs the snap count import + merge. Pass ?season= to backfill a prior year;
    /// omit it to use the calendar season (what the recurring job does).
    /// </summary>
    [HttpPost("jobs/run-snap-count-sync")]
    public async Task<IActionResult> RunSnapCountSync(
        [FromServices] SnapCountSyncJob snapCountJob,
        CancellationToken ct,
        [FromQuery] int? season = null)
    {
        if (season.HasValue && (season < 2020 || season > DateTime.UtcNow.Year))
            return BadRequest($"Season must be between 2020 and {DateTime.UtcNow.Year}.");

        logger.LogInformation("Admin triggered snap count sync — season {Season}",
            season.HasValue ? season.Value.ToString() : "calendar");

        var result = await snapCountJob.RunAsync(season);

        // The job used to log its failures and return void, so this endpoint answered
        // 200 "complete" on a run that wrote nothing. Surface the outcome instead.
        if (!result.Success)
        {
            return BadRequest(new
            {
                result.Season,
                result.Error,
                Message = $"Snap count sync FAILED for {result.Season}."
            });
        }

        return Ok(new
        {
            result.Season,
            result.Inserted,
            result.Replaced,
            result.Merged,
            result.Unmatched,
            Message = result.Inserted == 0 && result.Replaced == 0
                ? $"Snap count sync for {result.Season} completed but wrote no rows."
                : $"Snap count sync complete for {result.Season}."
        });
    }

    /// <summary>
    /// FAN-138 — runs usage metrics aggregation for an explicit season.
    /// The Hangfire recurring registration reads the season from the NFL context,
    /// which made backfilling a prior season impossible without repointing the whole
    /// site's context. Omit ?season= to fall back to the context season.
    /// </summary>
    [HttpPost("jobs/run-usage-metrics")]
    public async Task<IActionResult> RunUsageMetrics(
        [FromServices] UsageMetricsAggregationJob usageMetricsJob,
        [FromServices] INflContextService nflContext,
        CancellationToken ct,
        [FromQuery] int? season = null)
    {
        var targetSeason = season ?? await nflContext.GetSeasonAsync();

        if (targetSeason < 2020 || targetSeason > DateTime.UtcNow.Year)
            return BadRequest($"Season must be between 2020 and {DateTime.UtcNow.Year}.");

        logger.LogInformation(
            "Admin triggered usage metrics aggregation — season {Season}", targetSeason);

        var processed = await usageMetricsJob.ExecuteAsync(targetSeason);

        return Ok(new
        {
            Season = targetSeason,
            PlayersProcessed = processed,
            Message = processed == 0
                ? $"No game logs found for {targetSeason} — nothing was written. "
                  + "Run the stats sync for that season first."
                : $"Usage metrics aggregation complete for {targetSeason}."
        });
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

    // ── NFL schedule (FAN-178) ────────────────────────────────────────────

    /// <summary>
    /// Imports the regular-season schedule from nflverse. Runs inline rather
    /// than enqueued so the caller gets the counts back — a schedule import
    /// that silently half-worked is the failure worth catching immediately.
    /// </summary>
    [HttpPost("sync-nfl-schedule")]
    public async Task<IActionResult> SyncNflSchedule(
        [FromQuery] int? season,
        [FromServices] IMediator mediator,
        [FromServices] INflContextService nflContext,
        CancellationToken ct)
    {
        var targetSeason = season ?? await nflContext.GetSeasonAsync();

        logger.LogInformation(
            "Admin triggered NFL schedule sync — season {Season}", targetSeason);

        var result = await mediator.Send(new SyncNflScheduleCommand(targetSeason), ct);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                result.Season,
                result.Message,
                Hint = "nflverse publishes games.csv for the upcoming season well before "
                     + "Week 1. A zero-row parse usually means the column layout changed — "
                     + "check the logs for the required-column error."
            });
        }

        return Ok(new
        {
            result.Season,
            result.GamesImported,
            result.WeeksCovered,
            result.GamesFinal,
            result.GamesWithSpread,
            ElapsedSeconds = Math.Round(result.Elapsed.TotalSeconds, 1)
        });
    }

    /// <summary>
    /// Reads back one week of the stored schedule. Exists so the import can be
    /// verified against what the site actually shows without opening Compass.
    /// </summary>
    [HttpGet("nfl-schedule")]
    public async Task<IActionResult> GetNflSchedule(
        [FromQuery] int? season,
        [FromQuery] int? week,
        [FromServices] INflScheduleRepository scheduleRepository,
        [FromServices] INflContextService nflContext,
        CancellationToken ct)
    {
        var (contextSeason, contextWeek) = await nflContext.GetContextAsync();
        var targetSeason = season ?? contextSeason;
        var targetWeek = week ?? contextWeek;

        var games = await scheduleRepository.GetByWeekAsync(targetSeason, targetWeek, ct);
        var seasonCount = await scheduleRepository.CountBySeasonAsync(targetSeason, ct);

        return Ok(new
        {
            Season = targetSeason,
            Week = targetWeek,
            GamesInSeason = seasonCount,
            GamesInWeek = games.Count,
            Games = games.Select(g => new
            {
                g.GameId,
                Matchup = $"{g.AwayTeam} @ {g.HomeTeam}",
                g.Weekday,
                Gameday = g.Gameday.ToString("yyyy-MM-dd"),
                g.GametimeEt,
                g.SpreadLine,
                g.TotalLine,
                Score = g.IsFinal() ? $"{g.AwayScore}-{g.HomeScore}" : null
            })
        });
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

        SeedSeasonAverageSimsResult result;
        try
        {
            result = await mediator.Send(new SeedSeasonAverageSimsCommand(season), ct);
        }
        catch (NflverseDataUnavailableException ex)
        {
            // Asking for a season nflverse hasn't published is a caller mistake, not
            // a server fault — it was surfacing as an unhandled 500 with a stack trace.
            // A genuine connectivity failure IS a server-side problem and keeps its
            // own status so the two stop looking identical in the logs.
            if (ex.NotPublished)
            {
                logger.LogInformation(
                    "Season-average seed declined — nflverse has not published {Season} yet", season);
                return BadRequest(new { Season = season, Reason = "NotPublished", Message = ex.Message });
            }

            logger.LogError(ex,
                "Season-average seed could not reach nflverse for season {Season}", season);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { Season = season, Reason = "UpstreamUnavailable", Message = ex.Message });
        }

        return Ok(new
        {
            Message = $"Season-average sim seed complete for {season}.",
            result.Seeded,
            result.Skipped,
            result.Unmatched,
            result.MatchedByGsis,
            result.MatchedByName,
            result.AmbiguousSkipped
        });
    }

    [HttpPost("jobs/run-calibration")]
    public async Task<IActionResult> RunCalibration(
    [FromBody] RunCalibrationRequest request,
    [FromServices] IMediator mediator,
    CancellationToken ct)
    {
        // FAN-159: the basis is part of what the run means, so it is logged with
        // the trigger and echoed in the result. Default is Model — the value the
        // FantasyPros blend has not touched. "Blended" reproduces the pre-FAN-159
        // numbers and is grading the FP anchor against itself; it is here for
        // comparison, not for judging the model.
        var basis = string.IsNullOrWhiteSpace(request.ValueBasis)
            ? CalibrationValueBasis.Model
            : request.ValueBasis;

        logger.LogInformation(
            "Admin triggered calibration harness — season {Season}, format {Format}, " +
            "basis {Basis}, position {Position}",
            request.Season, request.ScoringFormat, basis, request.Position ?? "(all)");

        var result = await mediator.Send(
            new RunCalibrationCommand(
                request.Season, request.ScoringFormat ?? "Superflex", basis, request.Position), ct);

        logger.LogInformation(
            "Calibration complete on {Basis} basis, position {Position} — rho {Rho:F4}, " +
            "avg abs delta {Delta:F2}, top-10 overlap {Overlap}/10, {N} compared, {Unmatched} excluded",
            result.ValueBasis, result.Position ?? "(all)", result.SpearmanRho, result.AvgAbsDelta,
            result.Top10Overlap, result.PlayerCount, result.UnmatchedCount);

        // 2026-09-07: this hand-enumerated projection is why the unmatched-count
        // warning never appeared. UnmatchedCount and TopUnmatched were computed,
        // persisted to calibration_results, and returned by the handler — then
        // dropped here, at the API boundary, because the anonymous object lists
        // fields by hand and nobody added them. The client deserialised the
        // absent fields as 0/null and rendered nothing.
        //
        // Returning the result directly removes the class of bug rather than the
        // instance: any field the handler adds from now on reaches the client
        // without a second edit in a different project.
        return Ok(result);
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
    /// <param name="DisableVor">
    /// FAN-153 control experiment. Scores on the plain discounted career total —
    /// no replacement subtraction. A measurement run, not a serving state: it
    /// overwrites the live board, so re-run without the flag afterwards.
    /// </param>
    public record RunDfvRequest(
        int Season,
        string? ScoringFormat = null,
        bool DisableVor = false);

    /// <summary>
    /// FAN-153 phase 1. <c>Depth</c> and <c>TeamCount</c> both default rather than
    /// being required: depth to <see cref="PositionalValueCurveBuilder.DefaultDepth"/>,
    /// team count to 12 — and the team count affects only the preview in the
    /// response, never what is stored. The stored curve is league-agnostic on
    /// purpose; that is the whole design.
    /// </summary>
    public record BuildValueCurvesRequest(
        int Season, string? ScoringFormat = null, int? Depth = null, int? TeamCount = null);

    public record NflContextOverrideRequest(int? Season, int? Week);
    /// <summary>
    /// ValueBasis: "Model" (default — ModelValue, the FP blend removed but our
    /// guardrail caps applied), "Raw" (RawValue, one step earlier — before the
    /// positional guardrail caps, FAN-166) or "Blended" (TradeValue, what the
    /// site serves). An unrecognised value is rejected by the handler rather
    /// than defaulted, so a typo cannot silently fall back to the flattering
    /// basis. See FAN-159 and FAN-166.
    ///
    /// Position: "QB", "RB", "WR" or "TE" to restrict the run to one position,
    /// or null/empty for the whole board. A positional run removes the
    /// cross-position ladder from the comparison, so it measures only how well
    /// players are ordered within that position — run the same basis with and
    /// without it and the difference is the ladder. Also rejected rather than
    /// defaulted if unrecognised. See FAN-166.
    /// </summary>
    public record RunCalibrationRequest(
        int Season, string? ScoringFormat, string? ValueBasis = null, string? Position = null);
    public record AdminImportFantasyProsRequest(string CsvContent, int Season);
}