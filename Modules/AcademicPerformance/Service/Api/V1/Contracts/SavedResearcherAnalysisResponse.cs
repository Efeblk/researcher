using AcademicCollector.Analysis.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed record SavedResearcherAnalysisResponse(
    long Id,
    DateTimeOffset SavedAt,
    ResearcherAnalysisReport Report,
    ResearcherSourceCoverage? SourceCoverage = null);
