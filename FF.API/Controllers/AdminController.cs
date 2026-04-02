// FF.API/Controllers/AdminController.cs
using FF.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    /// <summary>
    /// Returns all registered users with role and Sleeper link status.
    /// Admin only.
    /// </summary>
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

    /// <summary>
    /// Assigns the Admin role to a user by email.
    /// Can only be called by an existing Admin.
    /// </summary>
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

    /// <summary>
    /// Removes Admin role from a user.
    /// </summary>
    [HttpPost("users/{email}/remove-admin")]
    public async Task<IActionResult> RemoveAdmin(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return NotFound($"User {email} not found.");

        await userManager.RemoveFromRoleAsync(user, "Admin");
        return Ok($"Admin role removed from {email}.");
    }
}