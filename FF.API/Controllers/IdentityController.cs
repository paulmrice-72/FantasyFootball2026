using FF.Application.Identity.Commands.LinkSleeperAccount;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/identity")]
[Authorize]
public class IdentityController(IMediator mediator) : ControllerBase
{
    [HttpPost("sleeper/link")]
    public async Task<IActionResult> LinkSleeperAccount(
        [FromBody] LinkSleeperAccountRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return Unauthorized();

        var result = await mediator.Send(
            new LinkSleeperAccountCommand(userId, request.SleeperUsername),
            cancellationToken);

        if (!result.Success)
            return BadRequest(new { result.ErrorMessage });

        return Ok(new
        {
            result.SleeperUserId,
            Message = "Sleeper account linked successfully."
        });
    }
}

public record LinkSleeperAccountRequest(string SleeperUsername);