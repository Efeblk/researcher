namespace ResearcherAnalysisService.Integrations.Gemini;

public interface IGeminiUsageRepository
{
    Task BeginAsync(Guid attemptId, DateTime startedAt, string requestedModel,
        CancellationToken cancellationToken);
    Task CompleteAsync(Guid attemptId, DateTime completedAt, GeminiUsageCompletion completion,
        CancellationToken cancellationToken);
    Task<GeminiSpendingStatus> GetSpendingAsync(CancellationToken cancellationToken);
}
