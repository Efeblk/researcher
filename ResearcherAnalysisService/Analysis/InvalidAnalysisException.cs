namespace ResearcherAnalysisService.Analysis;

public sealed class InvalidAnalysisException() : Exception("The AI provider returned an incomplete or unsupported report.");
