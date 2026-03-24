// FF.Infrastructure/Services/CoachRileyService.cs
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class CoachRileyService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<CoachRileyService> logger) : ICoachRileyService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    public async Task<string?> GenerateNarrativeAsync(
        WarRoomBriefDocument brief,
        CancellationToken ct = default)
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        var model = configuration["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Anthropic ApiKey not configured — skipping Coach Riley narrative");
            return null;
        }

        var prompt = BuildPrompt(brief);

        var requestBody = new
        {
            model,
            max_tokens = 300,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        try
        {
            var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError(
                    "Coach Riley API call failed — Status {Status}: {Body}",
                    (int)response.StatusCode, error);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);

            var narrative = result?.Content?.FirstOrDefault()?.Text?.Trim();

            logger.LogInformation(
                "Coach Riley narrative generated — {Length} chars",
                narrative?.Length ?? 0);

            return narrative;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Coach Riley narrative generation failed");
            return null;
        }
    }

    private static string BuildPrompt(WarRoomBriefDocument brief)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are Coach Riley, a sharp and confident fantasy football analyst.");
        sb.AppendLine($"Write a 3-4 sentence pregame narrative for a fantasy manager heading into Week {brief.Week} of the {brief.Season} NFL season.");
        sb.AppendLine("Be direct, punchy, and specific. Reference the players by name. No fluff.");
        sb.AppendLine("Do not include any markdown headers, titles, or formatting. Plain prose only.");
        sb.AppendLine();

        if (brief.TopBoomCandidates.Any())
        {
            sb.AppendLine("BOOM CANDIDATES (players to ride this week):");
            foreach (var p in brief.TopBoomCandidates)
            {
                sb.AppendLine($"- {p.PlayerName} ({p.Position}, {p.NflTeam} vs {p.OpponentTeam}) — {p.BoomProbability:P0} boom probability. {p.HighlightReason}");
            }
            sb.AppendLine();
        }

        if (brief.BustRisks.Any())
        {
            sb.AppendLine("BUST RISKS (players to be cautious about):");
            foreach (var p in brief.BustRisks)
            {
                sb.AppendLine($"- {p.PlayerName} ({p.Position}, {p.NflTeam} vs {p.OpponentTeam}) — {p.BustProbability:P0} bust probability. {p.HighlightReason}");
            }
            sb.AppendLine();
        }

        if (brief.Leagues.Any())
        {
            sb.AppendLine("LEAGUES:");
            foreach (var l in brief.Leagues)
            {
                sb.AppendLine($"- {l.LeagueName}: managing {l.TeamName}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Write the narrative now. 3-4 sentences maximum. Speak directly to the manager.");

        return sb.ToString();
    }

    // ── Response DTOs ─────────────────────────────────────────
    private class AnthropicResponse
    {
        public List<ContentBlock>? Content { get; set; }
    }

    private class ContentBlock
    {
        public string? Text { get; set; }
    }
}