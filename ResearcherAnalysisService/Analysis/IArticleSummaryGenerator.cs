using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public interface IArticleSummaryGenerator
{
    Task<GeneratedArticleChunk> GenerateAsync(string language, string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken);
}

public interface IArticleClaimVerifier
{
    Task<GeneratedVerificationBatch> VerifyAsync(string language, IReadOnlyList<GeneratedArticleClaim> claims,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken);
}

public sealed record GeneratedArticleClaim(string ClaimId, string Text, IReadOnlyList<string> SourceIds)
{
    public string? Section { get; init; }
}
public sealed record GeneratedArticleSections(IReadOnlyList<GeneratedArticleClaim> Purpose,
    IReadOnlyList<GeneratedArticleClaim> Methods, IReadOnlyList<GeneratedArticleClaim> Data,
    IReadOnlyList<GeneratedArticleClaim> Findings, IReadOnlyList<GeneratedArticleClaim> Limitations);
public sealed record GeneratedArticleChunk(GeneratedArticleSections Sections, string Model, string PromptVersion);

public sealed record GeneratedClaimVerdict(string ClaimId, string Verdict, string Reason);
public sealed record GeneratedVerificationBatch(IReadOnlyList<GeneratedClaimVerdict> Verdicts, string Model, string PromptVersion);
