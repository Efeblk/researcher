using System.Data;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class CanonicalArticleAnalysisQueryService(
    AnalysisDbContext database,
    ArticleSummaryAutomationStatusService statusService,
    CanonicalArticleEvidenceQueryService evidenceQueryService,
    ArticleReviewWorkflow reviewWorkflow)
{
    public async Task<CanonicalArticleAnalysisResponse?> GetAsync(
        string personelId,
        int canonicalWorkId,
        string language,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        ArticleSummaryAutomationStatusResponse? status = await statusService.GetAsync(
            personelId, canonicalWorkId, language, cancellationToken);
        if (status is null)
            return null;

        CanonicalArticleEvidenceResponse? evidence = await evidenceQueryService.GetLatestAsync(
            personelId, canonicalWorkId, language, skip, take, cancellationToken);
        CanonicalArticleReviewResponse? review = await reviewWorkflow.GetLatestAsync(
            personelId, canonicalWorkId, language, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(status, evidence, review);
    }
}
