using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed record SavedResearcherAnalysisResponse(
    long Id,
    DateTimeOffset SavedAt,
    ResearcherAnalysisReport Report,
    ResearcherSourceCoverage? SourceCoverage = null);
