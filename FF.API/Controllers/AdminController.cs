// FF.API/Controllers/AdminController.cs
using FF.Application.Interfaces.Persistence;
using FF.Infrastructure.Identity;
using FF.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(
    UserManager<ApplicationUser> userManager,
    IAppSettingsRepository appSettingsRepo) : ControllerBase
{
    // ── existing user endpoints unchanged ───────────────────────────────

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
        if (await userManager.IsInRoleAsync(user, "Admin"))
            return Ok($"{email} is already an Admin.");
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

    // ── NEW: NFL Context simulation override ─────────────────────────────

    /// <summary>Returns current app settings including any active simulation override.</summary>
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

    /// <summary>
    /// Sets a simulation override for season and/or week.
    /// Pass null for either field to clear that override.
    /// </summary>
    [HttpPost("nfl-context")]
    public async Task<IActionResult> SetNflContext([FromBody] NflContextOverrideRequest request)
    {
        if (request.Season.HasValue && (request.Season < 2020 || request.Season > 2030))
            return BadRequest("Season must be between 2020 and 2030.");
        if (request.Week.HasValue && (request.Week < 1 || request.Week > 18))
            return BadRequest("Week must be between 1 and 18.");

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

    /// <summary>Clears all simulation overrides — reverts to calendar-based season/week.</summary>
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


    public record NflContextOverrideRequest(int? Season, int? Week);
}