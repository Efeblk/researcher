using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

public sealed class AcademicWorkSource
{
    public int Id { get; set; }
    public int AcademicWorkId { get; set; }
    [JsonIgnore] public AcademicWork? AcademicWork { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Kind { get; set; } = "Unknown";
    public string Origin { get; set; } = string.Empty;
    public bool? IsOpenAccess { get; set; }
}
