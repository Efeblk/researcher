using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherMetrics
{
    public int Id { get; set; }
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    public int? WorksCount { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public int? I10Index { get; set; } = null;
    public string Source { get; set; } = "ORCID";
    public DateTime UpdatedAt { get; set; }
}
