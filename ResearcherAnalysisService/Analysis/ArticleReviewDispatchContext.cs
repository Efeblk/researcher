using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ResearcherAnalysisService.Analysis;

public sealed class ArticleReviewDispatchContext
{
    private bool _active;
    public Guid? AttemptId { get; private set; }
    public GeminiUsageCompletion? Completion { get; private set; }
    public ArticleReviewProviderAttempt? LastAttempt { get; private set; }

    public IDisposable Enter(Guid attemptId)
    {
        if (attemptId == Guid.Empty || _active)
            throw new InvalidOperationException("An article review dispatch is already active.");
        _active = true;
        AttemptId = attemptId;
        Completion = null;
        LastAttempt = new(attemptId, "Unknown", null, null, null);
        return new Scope(this);
    }

    public void Capture(GeminiUsageCompletion completion)
    {
        if (AttemptId.HasValue)
        {
            Completion = completion;
            LastAttempt = new(AttemptId.Value, completion.Outcome, completion.ReturnedModel,
                completion.EstimatedUsd, completion.PricingVersion);
        }
    }

    public ArticleReviewProviderAttempt Snapshot()
    {
        if (LastAttempt is null)
            throw new InvalidOperationException("No article review dispatch is active.");
        return LastAttempt;
    }

    private sealed class Scope(ArticleReviewDispatchContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._active = false;
        }
    }
}
