using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class OpenAlexProfileSummaryDto
{
    public string OpenAlexAuthorId { get; set; } = string.Empty;
    public string? DisplayName { get; set; } = null;
    public string? LastKnownInstitution { get; set; } = null;
    public int WorksCount { get; set; }
    public int CollectedWorksCount { get; set; }
    public int CitedByCount { get; set; }
    public int? HIndex { get; set; } = null;
    public int? I10Index { get; set; } = null;
    public decimal? TwoYearMeanCitedness { get; set; } = null;
    public DateTime LastUpdatedAt { get; set; }
}
