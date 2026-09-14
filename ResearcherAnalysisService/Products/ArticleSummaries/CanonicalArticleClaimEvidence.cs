using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class CanonicalArticleClaimEvidence
{
    public long Id { get; set; }
    public long CanonicalArticleClaimId { get; set; }

    [JsonIgnore]
    public CanonicalArticleClaim? CanonicalArticleClaim { get; set; } = null;

    public long ArticleSourceSpanId { get; set; }

    [JsonIgnore]
    public ArticleSourceSpanSnapshot? ArticleSourceSpan { get; set; } = null;

    public int Ordinal { get; set; }
}
