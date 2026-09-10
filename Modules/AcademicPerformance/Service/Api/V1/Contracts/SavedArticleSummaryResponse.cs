using AcademicCollector.Analysis.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed record SavedArticleSummaryResponse(long Id, int OriginalAcademicWorkId, string PersonelID,
    DateTimeOffset SavedAt, string? SourceUrl, ArticleSummaryReport Report);
