using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public sealed class PublicationMetricsRefreshState
{
    public string PersonelId { get; set; } = string.Empty;
    public Researcher? Researcher { get; set; } = null;
    public long RequestedRevision { get; set; }
    public long ComputedRevision { get; set; }
    public string RequestedCatalogVersion { get; set; } = string.Empty;
    public int RequestedComputationYear { get; set; }
    public long? LastSuccessfulSnapshotId { get; set; } = null;
    public PublicationMetricSnapshot? LastSuccessfulSnapshot { get; set; } = null;
    public DateTime? LastSuccessAt { get; set; } = null;
    public DateTime NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? LastOutcomeCode { get; set; } = null;
    public string? LastOutcomeMessage { get; set; } = null;
}
