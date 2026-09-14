using System.Text.Json;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class CanonicalArticleEvidenceQueryService(AnalysisDbContext database)
{
    private const int MaximumPageSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CanonicalArticleEvidenceResponse?> GetLatestAsync(
        string personelId,
        int canonicalWorkId,
        string language,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        bool hasCurrentAssociation = await database.CanonicalResearcherWorks.AsNoTracking()
            .AnyAsync(association => association.CanonicalWorkId == canonicalWorkId &&
                association.PersonelId == personelId, cancellationToken);
        if (!hasCurrentAssociation)
            return null;

        string? sourceIdentityHash = await CanonicalSourceIdentity.LoadAsync(
            database, canonicalWorkId, cancellationToken);
        if (sourceIdentityHash is null)
            return null;

        CanonicalArticleAnalysisRun? run = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(value => value.ArticleSourceSnapshot)
            .Where(value => value.CanonicalWorkId == canonicalWorkId && value.Language == language &&
                value.SourceIdentityHash == sourceIdentityHash)
            .OrderByDescending(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
            return null;

        int boundedSkip = Math.Max(0, skip);
        int boundedTake = Math.Clamp(take, 1, MaximumPageSize);
        IQueryable<CanonicalArticleClaim> claimQuery = database.CanonicalArticleClaims.AsNoTracking()
            .Where(claim => claim.CanonicalArticleAnalysisRunId == run.Id);
        int totalClaimCount = await claimQuery.CountAsync(cancellationToken);
        List<CanonicalArticleClaim> claims = await claimQuery
            .OrderBy(claim => claim.SectionOrder)
            .ThenBy(claim => claim.Ordinal)
            .ThenBy(claim => claim.Id)
            .Skip(boundedSkip)
            .Take(boundedTake)
            .Include(claim => claim.Evidence.OrderBy(evidence => evidence.Ordinal))
                .ThenInclude(evidence => evidence.ArticleSourceSpan)
            .ToListAsync(cancellationToken);
        ArticleSourceSnapshot source = run.ArticleSourceSnapshot!;

        return new()
        {
            PersonelId = personelId,
            CanonicalWorkId = canonicalWorkId,
            AnalysisRunId = run.Id,
            AnalyzedAt = run.AnalyzedAt,
            Language = run.Language,
            Model = run.Model,
            PromptVersion = run.PromptVersion,
            Skip = boundedSkip,
            Take = boundedTake,
            TotalClaimCount = totalClaimCount,
            Source = new()
            {
                Id = source.Id,
                ExtractedTextHash = source.ExtractedTextHash,
                SourceKind = source.SourceKind,
                ExtractionVersion = source.ExtractionVersion,
                ExtractionMethod = run.ExtractionMethod,
                Origin = run.SourceOrigin,
                AcquiredAt = run.SourceAcquiredAt,
                PageCount = await database.ArticleSourcePages.AsNoTracking()
                    .CountAsync(page => page.ArticleSourceSnapshotId == source.Id, cancellationToken),
                SpanCount = await database.ArticleSourceSpans.AsNoTracking()
                    .CountAsync(span => span.ArticleSourceSnapshotId == source.Id, cancellationToken)
            },
            Coverage = new()
            {
                ProcessedChunks = run.ProcessedChunks,
                TotalChunks = run.TotalChunks,
                ProcessedPages = run.ProcessedPages,
                TextBearingPages = run.TextBearingPages,
                TotalPages = run.TotalPages,
                SelectedClaimsOmitted = run.SelectedClaimsOmitted,
                IsPartial = run.IsPartial,
                ScopeReason = run.ScopeReason,
                CandidateClaims = run.CandidateClaims,
                AutomaticallyCheckedClaims = run.AutomaticallyCheckedClaims,
                SupportedClaims = run.SupportedClaims,
                UnsupportedClaims = run.UnsupportedClaims,
                UncertainClaims = run.UncertainClaims,
                DuplicateOrCappedClaims = run.DuplicateOrCappedClaims,
                BudgetUnverifiedClaims = run.BudgetUnverifiedClaims,
                OmissionReasons = JsonSerializer.Deserialize<List<string>>(
                    run.OmissionReasonsJson, JsonOptions) ?? []
            },
            Verifier = new()
            {
                Status = run.VerificationStatus,
                Model = run.VerificationModel,
                PromptVersion = run.VerificationPromptVersion,
                UsesSameModelFamily = run.UsesSameModelFamily,
                Limitation = run.VerificationLimitation
            },
            Claims = claims.Select(claim => new CanonicalArticleClaimDto
            {
                Id = claim.Id,
                Section = claim.Section,
                Ordinal = claim.Ordinal,
                ClaimId = claim.ExternalClaimId,
                Text = claim.Text,
                Evidence = claim.Evidence.OrderBy(evidence => evidence.Ordinal)
                    .Select(evidence => new CanonicalArticleEvidenceDto
                    {
                        SourceId = evidence.ArticleSourceSpan!.SourceId,
                        PageNumber = evidence.ArticleSourceSpan.PageNumber,
                        StartOffset = evidence.ArticleSourceSpan.StartOffset,
                        EndOffset = evidence.ArticleSourceSpan.EndOffset,
                        Quote = evidence.ArticleSourceSpan.Text
                    }).ToList()
            }).ToList()
        };
    }
}
