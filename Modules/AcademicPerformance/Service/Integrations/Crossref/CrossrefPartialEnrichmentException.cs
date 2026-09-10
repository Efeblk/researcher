namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;

public sealed class CrossrefPartialEnrichmentException(
    int completedCount, Exception innerException)
    : Exception(innerException.Message, innerException)
{
    public int CompletedCount { get; } = completedCount;
}
