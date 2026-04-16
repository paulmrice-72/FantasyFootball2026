// FF.Infrastructure/Agents/AgentOrchestrationService.cs
using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FF.Infrastructure.Agents;

/// <summary>
/// Implements multi-agent orchestration using direct Anthropic API calls.
/// Follows the same raw HTTP pattern as CoachRileyService — no SDK dependency.
/// Each agent turn is a stateless API call with full history in context.
/// </summary>
public class AgentOrchestrationService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<AgentOrchestrationService> logger) : IAgentOrchestrationService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const int MaxTokens = 600;   // per agent turn — enough for a solid paragraph

    // ── Single turn ───────────────────────────────────────────────────────
    public async Task<string?> RunAgentTurnAsync(
        string systemPrompt,
        IReadOnlyList<AgentMessage> history,
        CancellationToken ct = default)
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        var model = configuration["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Anthropic ApiKey not configured — skipping agent turn");
            return null;
        }

        // Anthropic requires alternating user/assistant roles — enforce it
        var messages = NormalizeHistory(history);

        var requestBody = new
        {
            model,
            max_tokens = MaxTokens,
            system = systemPrompt,
            messages
        };

        var http = httpClientFactory.CreateClient("AnthropicAgent");
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Agent API call failed — {Status}: {Body}",
                    (int)response.StatusCode, error);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);

            var text = result?.Content?.FirstOrDefault()?.Text?.Trim();
            logger.LogInformation("Agent turn complete — {Chars} chars", text?.Length ?? 0);
            return text;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent turn failed");
            return null;
        }
    }

    // ── Roundtable orchestration ──────────────────────────────────────────
    // Each persona takes one turn in sequence. Prior turns are appended to
    // the conversation history so each writer "reads" what came before.
    public async Task<IReadOnlyList<AgentTurn>> RunRoundtableAsync(
        string topic,
        IReadOnlyList<PersonaDefinition> personas,
        CancellationToken ct = default)
    {
        var turns = new List<AgentTurn>();
        var history = new List<AgentMessage>
        {
            new("user", $"Today's topic: {topic}\n\nShare your analysis and perspective.")
        };

        foreach (var persona in personas)
        {
            logger.LogInformation("Running agent turn for {Persona}", persona.Name);

            var response = await RunAgentTurnAsync(persona.SystemPrompt, history, ct);
            if (response is null)
            {
                logger.LogWarning("No response from {Persona} — skipping turn", persona.Name);
                continue;
            }

            turns.Add(new AgentTurn(persona.PersonaId, persona.Name, response, DateTime.UtcNow));

            // Append this turn to history so next persona has full context
            history.Add(new AgentMessage("assistant", response));
            history.Add(new AgentMessage("user",
                "Thank you. Now the next analyst will share their perspective."));
        }

        return turns;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    // Anthropic rejects messages if they don't strictly alternate user/assistant.
    // This coalesces consecutive same-role messages into one.
    private static List<object> NormalizeHistory(IReadOnlyList<AgentMessage> history)
    {
        var normalized = new List<object>();
        string? lastRole = null;
        var buffer = new StringBuilder();

        foreach (var msg in history)
        {
            if (msg.Role == lastRole)
            {
                buffer.Append('\n').Append(msg.Content);
            }
            else
            {
                if (lastRole is not null)
                    normalized.Add(new { role = lastRole, content = buffer.ToString() });
                buffer.Clear().Append(msg.Content);
                lastRole = msg.Role;
            }
        }

        if (lastRole is not null)
            normalized.Add(new { role = lastRole, content = buffer.ToString() });

        return normalized;
    }

    // ── Response DTOs ─────────────────────────────────────────────────────
    private class AnthropicResponse { public List<ContentBlock>? Content { get; set; } }
    private class ContentBlock { public string? Text { get; set; } }
}