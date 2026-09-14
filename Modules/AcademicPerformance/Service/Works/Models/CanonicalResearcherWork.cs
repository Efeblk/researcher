using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

public sealed class CanonicalResearcherWork
{
    public int CanonicalWorkId { get; set; }

    [JsonIgnore]
    public CanonicalWork? CanonicalWork { get; set; } = null;

    public string PersonelId { get; set; } = string.Empty;

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    public DateTime LastObservedAt { get; set; }
}
