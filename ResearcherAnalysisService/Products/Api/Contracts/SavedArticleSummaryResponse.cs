using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed record SavedArticleSummaryResponse(long Id, int OriginalAcademicWorkId, string PersonelID,
    DateTimeOffset SavedAt, string? SourceUrl, ArticleSummaryReport Report);
