// FF.Domain/Documents/ArticleRatingDocument.cs
namespace FF.Domain.Documents;

public class ArticleRatingDocument
{
    public string Id { get; set; } = string.Empty;      // "{articleId}::{userId}"
    public string ArticleId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public bool IsThumbsUp { get; set; }                // true = up, false = down
    public DateTime RatedAt { get; set; }
}