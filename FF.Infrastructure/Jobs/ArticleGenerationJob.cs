// FF.Infrastructure/Jobs/ArticleGenerationJob.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Generates one article per active writer persona.
/// Each writer receives a data payload curated to their specialty positions.
/// Fires Tuesdays 10am UTC. Safe to trigger manually from Admin Imports.
/// </summary>
public class ArticleGenerationJob(
    IWriterPersonaRepository personaRepo,
    IArticleRepository articleRepo,
    IDynastyValuationRepository valuationRepo,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ArticleGenerationJob> logger,
    INflContextService nflContextService,
    IPlatformSettingsRepository platformSettingsRepo)
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(CancellationToken ct = default, bool forceRun = false)
    {
        if (!forceRun)
        {
            var settings = await platformSettingsRepo.GetAsync();
            if (!settings.AiJobsEnabled)
            {
                logger.LogInformation("ArticleGenerationJob skipped — AiJobsEnabled is false");
                return;
            }
        }

        var (season, week) = await nflContextService.GetContextAsync();

        logger.LogInformation(
                "ArticleGenerationJob starting — Season {Season} Week {Week}", season, week);

        var apiKey = configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Anthropic ApiKey not configured — skipping article generation");
            return;
        }

        var personas = await personaRepo.GetAllActiveAsync(ct);
        if (personas.Count == 0)
        {
            logger.LogWarning("No active writer personas found — skipping");
            return;
        }

        // Pull top 200 valuations — no season param needed, collection is always current
        var allValuations = await valuationRepo.GetTopByTradeValueAsync(200);

        var generated = 0;
        var failed = 0;

        foreach (var persona in personas)
        {
            try
            {
                var article = await GenerateArticleAsync(
                    persona, allValuations, season, week, apiKey, ct);

                if (article is not null)
                {
                    await articleRepo.UpsertAsync(article, ct);
                    generated++;
                    logger.LogInformation(
                        "Article generated for {Persona} — {Title}",
                        persona.Name, article.Title);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Article generation failed for persona {Persona}", persona.Name);
                failed++;
            }
        }

        logger.LogInformation(
            "ArticleGenerationJob complete — {Generated} generated, {Failed} failed",
            generated, failed);
    }

    // ── Per-persona article generation ─────────────────────────────────────

    private async Task<ArticleDocument?> GenerateArticleAsync(
        WriterPersonaDocument persona,
        IReadOnlyList<DynastyValuationDocument> allValuations,
        int season,
        int week,
        string apiKey,
        CancellationToken ct)
    {
        // Filter to this writer's specialty positions
        var specialties = persona.Specialties
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var players = allValuations
            .Where(v => specialties.Contains("Dynasty") || specialties.Contains("Rookie")
                ? true  // Marcus Webb sees everyone
                : specialties.Any(s => v.Position.Equals(s, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => v.TradeValue)
            .Take(15)
            .ToList();

        if (players.Count == 0)
        {
            logger.LogWarning("No players found for {Persona} specialties: {Specialties}",
                persona.Name, persona.Specialties);
            return null;
        }

        var dataPayload = BuildDataPayload(persona, players, season, week, null, null);
        var (title, body) = await CallAnthropicAsync(persona, dataPayload, apiKey, ct);

        if (string.IsNullOrWhiteSpace(body))
            return null;

        return new ArticleDocument
        {
            Id = $"{persona.Id}-{season}-{week}",
            PersonaId = persona.Id,
            PersonaName = persona.Name,
            Role = persona.Role,
            Specialties = persona.Specialties,
            Title = title,
            Body = body,
            Season = season,
            Week = week,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private static string BuildDataPayload(
    WriterPersonaDocument persona,
    List<DynastyValuationDocument> players,
    int season, int week,
    string? adminNotes = null,
    string? newTopic = null)
    {
        var sb = new StringBuilder();

        // ── Persistent editorial feedback ──────────────────────────
        if (persona.PersistentFeedback.Count > 0)
        {
            sb.AppendLine("EDITORIAL GUIDELINES (always follow these):");
            foreach (var f in persona.PersistentFeedback.TakeLast(10))
                sb.AppendLine($"- {f.Comment}");
            sb.AppendLine();
        }

        // ── One-time admin notes ───────────────────────────────────
        if (!string.IsNullOrWhiteSpace(adminNotes))
        {
            sb.AppendLine("EDITOR FEEDBACK ON PREVIOUS DRAFT (address this):");
            sb.AppendLine(adminNotes);
            sb.AppendLine();
        }

        // ── Topic override ─────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(newTopic))
        {
            sb.AppendLine($"REQUESTED TOPIC: {newTopic}");
            sb.AppendLine();
        }

        sb.AppendLine($"SEASON: {season} | WEEK: {week}");
        sb.AppendLine($"YOUR ROLE: {persona.Role}");
        sb.AppendLine();
        sb.AppendLine("TOP PLAYERS BY DYNASTY TRADE VALUE (your specialty positions):");
        sb.AppendLine("Name | Pos | Team | Age | Trade Value | Career Phase | Years Prime Remaining | Breakout Score | Breakout Signals");

        foreach (var p in players)
        {
            var signals = p.BreakoutSignals.Any()
                ? string.Join("; ", p.BreakoutSignals) : "none";
            sb.AppendLine(
                $"{p.PlayerName} | {p.Position} | {p.NflTeam} | Age {p.Age} | " +
                $"TV:{p.TradeValue:F1} | {p.CareerPhase} | " +
                $"{p.YearsOfPrimeRemaining:F1}yr prime | " +
                $"Breakout:{p.BreakoutScore:F0} | {signals}");
        }

        sb.AppendLine();
        sb.AppendLine("Write a comprehensive weekly feature article covering 2-3 of the most interesting stories in this data.");
        sb.AppendLine("Format your response using simple HTML only — use <h3> for section headers, <p> for paragraphs, <strong> for emphasis. No markdown. No ** or ## characters.");
        sb.AppendLine("Respond in this exact format:");
        sb.AppendLine("TITLE: <your plain text article title, no HTML>");
        sb.AppendLine("BODY: <your full HTML article body>");

        return sb.ToString();
    }

    private async Task<(string Title, string Body)> CallAnthropicAsync(
        WriterPersonaDocument persona,
        string dataPayload,
        string apiKey,
        CancellationToken ct)
    {
        var model = configuration["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";

        var requestBody = new
        {
            model,
            max_tokens = 1200,  // was 600
            system = persona.SystemPrompt,
            messages = new[]
            {
        new { role = "user", content = dataPayload }
    }
        };

        var http = httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Anthropic call failed for {Persona} — {Status}: {Body}",
                persona.Name, (int)response.StatusCode, error);
            return (string.Empty, string.Empty);
        }

        var result = await response.Content
            .ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);

        var raw = result?.Content?.FirstOrDefault()?.Text?.Trim() ?? string.Empty;

        return ParseTitleAndBody(raw);
    }

    private static (string Title, string Body) ParseTitleAndBody(string raw)
    {
        var title = string.Empty;
        var body = string.Empty;

        var titleIdx = raw.IndexOf("TITLE:", StringComparison.OrdinalIgnoreCase);
        var bodyIdx = raw.IndexOf("BODY:", StringComparison.OrdinalIgnoreCase);

        if (titleIdx >= 0 && bodyIdx > titleIdx)
        {
            title = raw[(titleIdx + 6)..bodyIdx].Trim();
            body = raw[(bodyIdx + 5)..].Trim();
        }
        else
        {
            // Fallback — treat whole response as body
            body = raw;
        }

        return (title, body);
    }
    /// <summary>
    /// Regenerates a single article for a specific persona,
    /// incorporating admin notes and optional new topic.
    /// </summary>
    public async Task RegenerateAsync(
        string personaId, string articleId,
        string adminNotes, string? newTopic,
        CancellationToken ct = default)
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        var persona = await personaRepo.GetByIdAsync(personaId, ct);
        if (persona is null) return;

        var (season, week) = await nflContextService.GetContextAsync();
        var allValuations = await valuationRepo.GetTopByTradeValueAsync(200);

        var specialties = persona.Specialties
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var players = allValuations
            .Where(v => specialties.Contains("Dynasty") || specialties.Contains("Rookie")
                ? true
                : specialties.Any(s => v.Position.Equals(s, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => v.TradeValue)
            .Take(15)
            .ToList();

        var dataPayload = BuildDataPayload(persona, players, season, week, adminNotes, newTopic);
        var (title, body) = await CallAnthropicAsync(persona, dataPayload, apiKey, ct);

        if (string.IsNullOrWhiteSpace(body)) return;

        var article = new ArticleDocument
        {
            Id = articleId,
            PersonaId = persona.Id,
            PersonaName = persona.Name,
            Role = persona.Role,
            Specialties = persona.Specialties,
            Title = title,
            Body = body,
            Season = season,
            Week = week,
            ReviewStatus = ArticleReviewStatus.Draft,
            GeneratedAt = DateTime.UtcNow
        };

        await articleRepo.UpsertAsync(article, ct);
    }
    // ── Response DTOs ───────────────────────────────────────────────────────
    private class AnthropicResponse
    {
        public List<ContentBlock>? Content { get; set; }
    }

    private class ContentBlock
    {
        public string? Text { get; set; }
    }
}