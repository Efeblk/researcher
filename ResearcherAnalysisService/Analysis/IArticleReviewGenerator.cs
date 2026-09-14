using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public interface IArticleReviewGenerator
{
    Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language, string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken);
}

public sealed record GeneratedArticleReviewFinding(
    string FindingId,
    string Role,
    string Kind,
    string Basis,
    string? Suggestion,
    IReadOnlyList<string> SourceIds);

public sealed record GeneratedArticleReviewPass(
    string Role,
    IReadOnlyList<GeneratedArticleReviewFinding> Findings,
    string Model,
    string PromptVersion);
