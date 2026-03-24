// FF.API/Controllers/BriefController.cs
using FF.Application.Features.Brief;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/brief")]
[Authorize]
public class BriefController(
    IWarRoomBriefRepository briefRepository,
    WarRoomBriefService briefService,
    IEmailService emailService,
    IMediator mediator) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(
        [FromQuery] int season,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var brief = await briefRepository.GetLatestAsync(userId, season, ct);
        if (brief is null) return NotFound("No brief found. Trigger generation first.");

        return Ok(brief);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(
        [FromQuery] int season,
        [FromQuery] int week,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var brief = await briefService.GenerateBriefAsync(
            userId, email, season, week, ct);

        return Ok(brief);
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await mediator.Send(
            new GetDeliveryPreferencesQuery(userId), ct);
        return Ok(result);
    }

    [HttpPost("preferences")]
    public async Task<IActionResult> UpsertPreferences(
        [FromBody] UpsertDeliveryPreferencesRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await mediator.Send(new UpsertDeliveryPreferencesCommand(
            userId,
            request.EmailEnabled,
            request.DeliveryDayOfWeek,
            request.DeliveryHourUtc,
            request.TimeZoneId,
            request.IncludeBoomCandidates,
            request.IncludeBustRisks,
            request.IncludeLeagueSections,
            request.IncludeCoachRiley), ct);

        return result.Success ? Ok() : BadRequest(result.ErrorMessage);
    }

    public record UpsertDeliveryPreferencesRequest(
        bool EmailEnabled,
        int DeliveryDayOfWeek,
        int DeliveryHourUtc,
        string TimeZoneId,
        bool IncludeBoomCandidates,
        bool IncludeBustRisks,
        bool IncludeLeagueSections,
        bool IncludeCoachRiley);
}