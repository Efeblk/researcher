using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public interface IArticleReviewRecoveryGenerator : IArticleReviewGenerator
{
    Task<GeneratedArticleReviewPass> GenerateAsync(
        string role,
        string language,
        string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans,
        string thinkingLevel,
        CancellationToken cancellationToken);
}
