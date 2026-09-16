namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed class SemanticScholarPartialEnrichmentException(int completedCount, Exception innerException)
    : Exception("Semantic Scholar collection made partial progress and can be resumed.", innerException)
{
    public int CompletedCount { get; } = completedCount;
    public SemanticScholarRequestException? RequestFailure { get; } =
        FindRequestFailure(innerException);

    private static SemanticScholarRequestException? FindRequestFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SemanticScholarRequestException failure) return failure;
        return null;
    }
}
