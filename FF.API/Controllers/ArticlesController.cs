// FF.API/Controllers/ArticlesController.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Infrastructure.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/articles")]
public class ArticlesController(
    IArticleRepository articleRepo,
    IArticleRatingRepository ratingRepo,
    ILogger<ArticlesController> logger,
    IWriterPersonaRepository personaRepo) : ControllerBase
{
    // ── Public endpoints ───────────────────────────────────────────────────

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(
        [FromQuery] int count = 10, CancellationToken ct = default)
    {
        var articles = await articleRepo.GetLatestAsync(count, ct);
        return Ok(articles);
    }

    [HttpGet("season/{season}/week/{week}")]
    public async Task<IActionResult> GetBySeasonWeek(
        int season, int week, CancellationToken ct = default)
    {
        var articles = await articleRepo.GetBySeasonWeekAsync(season, week, ct);
        return Ok(articles);
    }

    // ── Ratings (authenticated users) ──────────────────────────────────────

    [HttpPost("{id}/rate")]
    [Authorize]
    public async Task<IActionResult> Rate(
        string id, [FromBody] RateRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Check if already rated
        var existing = await ratingRepo.GetAsync(id, userId, ct);
        if (existing is not null)
            return Conflict(new { Message = "You have already rated this article." });

        await ratingRepo.UpsertAsync(new ArticleRatingDocument
        {
            ArticleId = id,
            UserId = userId,
            IsThumbsUp = request.IsThumbsUp
        }, ct);

        await articleRepo.IncrementRatingAsync(id, request.IsThumbsUp, ct);

        return Ok(new { Message = "Rating recorded." });
    }

    [HttpGet("{id}/my-rating")]
    [Authorize]
    public async Task<IActionResult> GetMyRating(string id, CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rating = await ratingRepo.GetAsync(id, userId, ct);
        return Ok(new { HasRated = rating is not null, IsThumbsUp = rating?.IsThumbsUp });
    }

    // ── Admin endpoints ────────────────────────────────────────────────────

    [HttpGet("admin/all")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAllForReview(CancellationToken ct = default)
    {
        try
        {
            var articles = await articleRepo.GetAllForReviewAsync(ct);
            return Ok(articles);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetAllForReview failed");
            return StatusCode(500, new { Error = ex.Message, Inner = ex.InnerException?.Message, Stack = ex.StackTrace });
        }
    }

    [HttpPost("admin/{id}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(string id, CancellationToken ct = default)
    {
        var reviewedBy = User.Identity?.Name ?? "admin";
        await articleRepo.SetReviewStatusAsync(id, ArticleReviewStatus.Approved, reviewedBy, ct);
        return NoContent();
    }

    [HttpPost("admin/{id}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(string id, CancellationToken ct = default)
    {
        var reviewedBy = User.Identity?.Name ?? "admin";
        await articleRepo.SetReviewStatusAsync(id, ArticleReviewStatus.Rejected, reviewedBy, ct);
        return NoContent();
    }
    [HttpPost("admin/{id}/regenerate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Regenerate(
    string id,
    [FromBody] RegenerateRequest request,
    [FromServices] ArticleGenerationJob articleJob,
    CancellationToken ct = default)
    {
        var reviewedBy = User.Identity?.Name ?? "admin";

        // Save notes to the article first
        await articleRepo.SetAdminNotesAsync(id, request.AdminNotes, request.NewTopic, ct);

        // If this feedback should persist, append to writer persona
        if (request.SaveAsPersistentFeedback && !string.IsNullOrWhiteSpace(request.AdminNotes))
        {
            await personaRepo.AddFeedbackAsync(request.PersonaId, new WriterFeedbackEntry
            {
                Comment = request.AdminNotes,
                AddedAt = DateTime.UtcNow,
                AddedBy = reviewedBy
            }, ct);
        }

        // Regenerate in background so the HTTP call returns immediately
        _ = Task.Run(async () =>
        {
            await articleJob.RegenerateAsync(
                request.PersonaId, id,
                request.AdminNotes, request.NewTopic,
                CancellationToken.None);
        });

        return Accepted(new { Message = "Regeneration started. Refresh in ~10 seconds." });
    }

    [HttpDelete("admin/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Scrap(string id, CancellationToken ct = default)
    {
        await articleRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    // Single article by ID — public, for share links
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct = default)
    {
        var article = await articleRepo.GetByIdAsync(id, ct);
        if (article is null) return NotFound();
        if (article.ReviewStatus != ArticleReviewStatus.Approved) return NotFound();
        return Ok(article);
    }

    // Writer archive
    [HttpGet("writer/{personaId}")]
    public async Task<IActionResult> GetByWriter(
        string personaId,
        [FromQuery] int? season = null,
        [FromQuery] int? month = null,
        CancellationToken ct = default)
    {
        var articles = await articleRepo.GetByPersonaAsync(personaId, season, month, ct);
        return Ok(articles);
    }
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Returns count of approved articles the user hasn't rated yet — powers the nav badge.</summary>
    [HttpGet("unread-count")]
    [AllowAnonymous]  // ← was [Authorize]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Ok(new { Count = 0 });  // not logged in — no badge

        var articles = await articleRepo.GetLatestAsync(20, ct);
        var unread = 0;
        foreach (var article in articles)
        {
            var rating = await ratingRepo.GetAsync(article.Id, userId, ct);
            if (rating is null) unread++;
        }
        return Ok(new { Count = unread });
    }
    public record RegenerateRequest(
        string PersonaId,
        string AdminNotes,
        string? NewTopic,
        bool SaveAsPersistentFeedback);
    public record RateRequest(bool IsThumbsUp);
}