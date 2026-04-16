// FF.Application/Interfaces/Services/IAgentOrchestrationService.cs
namespace FF.Application.Interfaces.Services;

/// <summary>
/// Orchestrates multi-turn AI agent conversations for the Writers' Room (Epic 15).
/// Each writer persona runs as an agent turn with accumulated context.
/// </summary>
public interface IAgentOrchestrationService
{
    /// <summary>
    /// Runs a single agent turn given a persona system prompt and the full
    /// conversation history so far. Returns the agent's response text.
    /// </summary>
    Task<string?> RunAgentTurnAsync(
        string systemPrompt,
        IReadOnlyList<AgentMessage> history,
        CancellationToken ct = default);

    /// <summary>
    /// Runs a sequential roundtable: each persona takes a turn in order,
    /// with prior turns appended to context. Returns all turns in order.
    /// </summary>
    Task<IReadOnlyList<AgentTurn>> RunRoundtableAsync(
        string topic,
        IReadOnlyList<PersonaDefinition> personas,
        CancellationToken ct = default);
}

public record AgentMessage(string Role, string Content);  // role: "user" | "assistant"

public record PersonaDefinition(string PersonaId, string Name, string SystemPrompt);

public record AgentTurn(string PersonaId, string PersonaName, string Content, DateTime ProducedAt);