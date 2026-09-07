namespace ResearcherAnalysisService.Analysis;

public sealed class AnalysisInputTooLargeException() : Exception(
    "The publication sample is too large for the configured local context. Submit fewer publications or increase Ai:OllamaContextTokens within your model and hardware limits.");
