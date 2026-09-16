using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed class CanonicalArticleEvidenceResponse
{
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public int CanonicalWorkId { get; set; }
    public long AnalysisRunId { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    public ArticleEvidenceSourceDto Source { get; set; } = new();
    public ArticleEvidenceCoverageDto Coverage { get; set; } = new();
    public ArticleEvidenceVerifierDto Verifier { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int TotalClaimCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<CanonicalArticleClaimDto> Claims { get; set; } = [];
}

public sealed class ArticleEvidenceSourceDto
{
    public long Id { get; set; }
    public string ExtractedTextHash { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string ExtractionVersion { get; set; } = string.Empty;
    public string ExtractionMethod { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public DateTimeOffset AcquiredAt { get; set; }
    public int PageCount { get; set; }
    public int SpanCount { get; set; }
}

public sealed class ArticleEvidenceCoverageDto
{
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
    public List<string> OmissionReasons { get; set; } = [];
}

public sealed class ArticleEvidenceVerifierDto
{
    public string Status { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public bool UsesSameModelFamily { get; set; }
    public string? Limitation { get; set; } = null;
}

public sealed class CanonicalArticleClaimDto
{
    public long Id { get; set; }
    public string Section { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string? ClaimId { get; set; } = null;
    public string Text { get; set; } = string.Empty;
    public List<CanonicalArticleEvidenceDto> Evidence { get; set; } = [];
}

public sealed class CanonicalArticleEvidenceDto
{
    public string SourceId { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string Quote { get; set; } = string.Empty;
}
