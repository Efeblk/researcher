using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;

public sealed class CanonicalArticleReviewEvidence
{
    public long Id { get; set; }
    public long CanonicalArticleReviewFindingId { get; set; }
    [JsonIgnore] public CanonicalArticleReviewFinding? CanonicalArticleReviewFinding { get; set; } = null;
    public long ArticleSourceSpanId { get; set; }
    [JsonIgnore] public ArticleSourceSpanSnapshot? ArticleSourceSpan { get; set; } = null;
    public int Ordinal { get; set; }
}
