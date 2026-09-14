using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class CanonicalArticleClaim
{
    public long Id { get; set; }
    public long CanonicalArticleAnalysisRunId { get; set; }

    [JsonIgnore]
    public CanonicalArticleAnalysisRun? CanonicalArticleAnalysisRun { get; set; } = null;

    public string Section { get; set; } = string.Empty;
    public int SectionOrder { get; set; }
    public int Ordinal { get; set; }
    public string? ExternalClaimId { get; set; } = null;
    public string Text { get; set; } = string.Empty;
    public List<CanonicalArticleClaimEvidence> Evidence { get; set; } = [];
}
