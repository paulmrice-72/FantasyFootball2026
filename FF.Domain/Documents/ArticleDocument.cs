// FF.Domain/Documents/ArticleDocument.cs
using System.Text.Json.Serialization;
using FF.Domain.Enums;

namespace FF.Domain.Documents;

public class ArticleDocument
{
    public string Id { get; set; } = string.Empty;
    public string PersonaId { get; set; } = string.Empty;
    public string PersonaName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Specialties { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Week { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ArticleReviewStatus ReviewStatus { get; set; } = ArticleReviewStatus.Draft;

    public bool IsPublished => ReviewStatus == ArticleReviewStatus.Approved;
    public int ThumbsUp { get; set; }
    public int ThumbsDown { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }

    /// <summary>One-time note used when requesting regeneration of this article.</summary>
    public string? AdminNotes { get; set; }

    /// <summary>Optional specific topic/angle to use instead of the default data payload.</summary>
    public string? NewTopic { get; set; }
}