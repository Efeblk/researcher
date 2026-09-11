namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed class SemanticScholarPartialEnrichmentException(int completedCount, Exception innerException)
    : Exception($"Semantic Scholar enrichment stopped after {completedCount} DOI(s): {innerException.Message}", innerException)
{
    public int CompletedCount { get; } = completedCount;
}
