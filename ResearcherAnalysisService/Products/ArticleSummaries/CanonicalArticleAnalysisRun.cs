using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class CanonicalArticleAnalysisRun
{
    public long Id { get; set; }
    public int CanonicalWorkId { get; set; }

    public long ArticleSourceSnapshotId { get; set; }

    [JsonIgnore]
    public ArticleSourceSnapshot? ArticleSourceSnapshot { get; set; } = null;

    public long SavedArticleSummaryId { get; set; }

    [JsonIgnore]
    public SavedArticleSummary? SavedArticleSummary { get; set; } = null;

    public DateTimeOffset AnalyzedAt { get; set; }
    public DateTimeOffset SourceAcquiredAt { get; set; }
    public string? SourceUrl { get; set; } = null;
    public string SourceOrigin { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? PolicyVersion { get; set; } = null;
    public string? SourceIdentityHash { get; set; } = null;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string ExtractionMethod { get; set; } = string.Empty;
    public int ProcessedChunks { get; set; }
    public int TotalChunks { get; set; }
    public int ProcessedPages { get; set; }
    public int TextBearingPages { get; set; }
    public int TotalPages { get; set; }
    public int SelectedClaimsOmitted { get; set; }
    public bool IsPartial { get; set; }
    public string? ScopeReason { get; set; } = null;
    public int? CandidateClaims { get; set; } = null;
    public int? AutomaticallyCheckedClaims { get; set; } = null;
    public int? SupportedClaims { get; set; } = null;
    public int? UnsupportedClaims { get; set; } = null;
    public int? UncertainClaims { get; set; } = null;
    public int? DuplicateOrCappedClaims { get; set; } = null;
    public int? BudgetUnverifiedClaims { get; set; } = null;
    public string OmissionReasonsJson { get; set; } = "[]";
    public string VerificationStatus { get; set; } = string.Empty;
    public string VerificationModel { get; set; } = string.Empty;
    public string VerificationPromptVersion { get; set; } = string.Empty;
    public bool UsesSameModelFamily { get; set; }
    public string? VerificationLimitation { get; set; } = null;
    public List<CanonicalArticleClaim> Claims { get; set; } = [];
}
