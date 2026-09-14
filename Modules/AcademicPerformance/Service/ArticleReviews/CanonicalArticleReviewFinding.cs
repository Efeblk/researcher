using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;

public sealed class CanonicalArticleReviewFinding
{
    public long Id { get; set; }
    public long CanonicalArticleReviewRunId { get; set; }
    [JsonIgnore] public CanonicalArticleReviewRun? CanonicalArticleReviewRun { get; set; } = null;
    public int Ordinal { get; set; }
    public string ExternalFindingId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Basis { get; set; } = string.Empty;
    public string? Suggestion { get; set; } = null;
    public List<CanonicalArticleReviewEvidence> Evidence { get; set; } = [];
}
