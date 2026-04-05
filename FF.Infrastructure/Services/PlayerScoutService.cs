// FF.Infrastructure/Services/PlayerScoutService.cs
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class PlayerScoutService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PlayerScoutService> logger) : IPlayerScoutService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    public async Task<string?> GeneratePlayerNarrativeAsync(
        string sleeperPlayerId,
        string fullName,
        string position,
        string? nflTeam,
        int? age,
        string? collegeTeam,
        int? draftRound,
        int? draftPick,
        double dynastyScore,
        double draftCapitalScore,
        double positionalScore,
        double valuationBlendScore,
        double fantasyProsScore,
        int? fantasyProsRank,
        CancellationToken ct = default)
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        var model = configuration["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Anthropic ApiKey not configured — skipping player narrative");
            return null;
        }

        var prompt = BuildPrompt(fullName, position, nflTeam, age, collegeTeam,
            draftRound, draftPick, dynastyScore, draftCapitalScore,
            positionalScore, valuationBlendScore, fantasyProsScore, fantasyProsRank);

        var requestBody = new
        {
            model,
            max_tokens = 250,
            messages = new[] { new { role = "user", content = prompt } }
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
                logger.LogError("PlayerScout API call failed — {Status}: {Body}",
                    (int)response.StatusCode, error);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);

            var narrative = result?.Content?.FirstOrDefault()?.Text?.Trim();

            logger.LogInformation("Player narrative generated for {Player} — {Chars} chars",
                fullName, narrative?.Length ?? 0);

            return narrative;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Player narrative generation failed for {Player}", fullName);
            return null;
        }
    }

    private static string BuildPrompt(
        string fullName, string position, string? nflTeam,
        int? age, string? collegeTeam,
        int? draftRound, int? draftPick,
        double dynastyScore, double draftCapitalScore,
        double positionalScore, double valuationBlendScore,
        double fantasyProsScore, int? fantasyProsRank)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a sharp dynasty fantasy football analyst writing a player scouting report.");
        sb.AppendLine("Write 3-4 punchy sentences about this rookie's dynasty value. Be specific and direct.");
        sb.AppendLine("Reference the player by name. No markdown, no headers, plain prose only.");
        sb.AppendLine();
        sb.AppendLine("PLAYER PROFILE:");
        sb.AppendLine($"- Name: {fullName}");
        sb.AppendLine($"- Position: {position}");
        sb.AppendLine($"- NFL Team: {nflTeam ?? "Undrafted/TBD"}");
        sb.AppendLine($"- Age: {(age.HasValue ? age.ToString() : "Unknown")}");
        sb.AppendLine($"- College: {collegeTeam ?? "Unknown"}");

        if (draftRound.HasValue && draftPick.HasValue)
            sb.AppendLine($"- NFL Draft: Round {draftRound}, Pick {draftPick}");
        else
            sb.AppendLine("- NFL Draft: Undrafted or pick not yet recorded");

        sb.AppendLine();
        sb.AppendLine("DYNASTY ANALYTICS:");
        sb.AppendLine($"- Dynasty Score: {dynastyScore:F1}/100 (composite rating)");
        sb.AppendLine($"- Draft Capital Score: {draftCapitalScore:F1}/100 (pick slot value)");
        sb.AppendLine($"- Positional Value: {positionalScore:F1}/100 (dynasty scarcity)");
        sb.AppendLine($"- Valuation Blend: {valuationBlendScore:F1}/100 (career + trade + DFV)");

        if (fantasyProsRank.HasValue)
            sb.AppendLine($"- FantasyPros Dynasty Rank: #{fantasyProsRank} (score: {fantasyProsScore:F1}/100)");

        sb.AppendLine();
        sb.AppendLine("Write the scouting report now. 3-4 sentences. Speak to a dynasty manager.");

        return sb.ToString();
    }

    private class AnthropicResponse
    {
        public List<ContentBlock>? Content { get; set; }
    }

    private class ContentBlock
    {
        public string? Text { get; set; }
    }
}