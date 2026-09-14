namespace ResearcherAnalysisService.Products.Analysis;

public sealed class AnalysisInputUnavailableException() : Exception(
    "No saved publications fit the analysis input limits. Collect publication data first or adjust AnalysisService limits.");
