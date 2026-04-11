// FF.Domain/Documents/WriterPersonaDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// Defines a named AI writer persona for the Writers' Room (Epic 13).
/// Collection: writer_personas
/// </summary>
public class WriterPersonaDocument
{
    public string Id { get; set; } = string.Empty;         // e.g. "sam-caruso"
    public string Name { get; set; } = string.Empty;       // "Sam Caruso"
    public string Role { get; set; } = string.Empty;       // "RB Analyst"
    public string Specialties { get; set; } = string.Empty; // "RB"
    public string Voice { get; set; } = string.Empty;      // terse descriptor of writing style
    public string SystemPrompt { get; set; } = string.Empty; // full prompt injected into Anthropic calls
    public bool IsActive { get; set; } = true;
    public bool IsModerator { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}