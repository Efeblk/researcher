using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public interface IArticleReviewVerifier
{
    Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
        IReadOnlyList<GeneratedArticleReviewFinding> findings,
        IReadOnlyList<ArticleSourceSpan> sourceSpans,
        CancellationToken cancellationToken);
}

public sealed record GeneratedArticleReviewVerdict(string FindingId, string Verdict, string Reason);

public sealed record GeneratedArticleReviewVerification(
    IReadOnlyList<GeneratedArticleReviewVerdict> Verdicts,
    string Model,
    string PromptVersion);
