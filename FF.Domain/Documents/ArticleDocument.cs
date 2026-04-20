// FF.Domain/Documents/ArticleDocument.cs
namespace FF.Domain.Documents;

/// <summary>
/// A generated article from a Writers' Room persona.
/// Collection: articles
/// </summary>
public class ArticleDocument
{
    public string Id { get; set; } = string.Empty;        // e.g. "sam-caruso-2026-18"
    public string PersonaId { get; set; } = string.Empty; // "sam-caruso"
    public string PersonaName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Specialties { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Week { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime GeneratedAt { get; set; }
}