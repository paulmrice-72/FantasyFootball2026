// FF.Domain/Documents/WriterPersonaDocument.cs
namespace FF.Domain.Documents;

public class WriterPersonaDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Specialties { get; set; } = string.Empty;
    public string Voice { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsModerator { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Persistent editorial feedback from admin review.
    /// Prepended to every future article prompt for this writer.
    /// </summary>
    public List<WriterFeedbackEntry> PersistentFeedback { get; set; } = [];
}

public class WriterFeedbackEntry
{
    public string Comment { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
}