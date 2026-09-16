using AcademicCollector.Analysis.Contracts;
using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed record SavedResearcherAnalysisResponse(
    long Id,
    DateTimeOffset SavedAt,
    ResearcherAnalysisReport Report,
    ResearcherSourceCoverage? SourceCoverage = null);

public sealed record ResearcherAnalysisReadResponse(
    ResearcherSourceCoverage Coverage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] SavedResearcherAnalysisResponse? Analysis);
