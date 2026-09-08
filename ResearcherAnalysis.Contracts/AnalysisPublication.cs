using System.ComponentModel.DataAnnotations;

namespace AcademicCollector.Analysis.Contracts;

public sealed class AnalysisPublication
{
    [Required, StringLength(80), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    public string Title { get; set; } = string.Empty;

    [Range(1000, 3000)]
    public int? Year { get; set; } = null;

    [Required, StringLength(100)]
    public string Category { get; set; } = "Unknown";

    [StringLength(12000)]
    public string? Abstract { get; set; } = null;

    [StringLength(2000)]
    public string? Keywords { get; set; } = null;

    [StringLength(300)]
    public string? Doi { get; set; } = null;

    [Required, StringLength(300)]
    public string Sources { get; set; } = string.Empty;
}
