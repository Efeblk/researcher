using System.ComponentModel.DataAnnotations;

namespace AcademicCollector.Analysis.Contracts;

public sealed class ProviderMetrics
{
    [Required, StringLength(100)]
    public string Provider { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int? CitationCount { get; set; } = null;

    [Range(0, int.MaxValue)]
    public int? HIndex { get; set; } = null;

    public DateTimeOffset? CollectedAt { get; set; } = null;
}
