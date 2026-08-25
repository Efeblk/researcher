using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisRecord
{
    public int Id { get; set; }
    public int ResearcherId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public int RecordIndex { get; set; }
    public string? ExternalRecordId { get; set; } = null;
    public string RecordJson { get; set; } = "{}";
    public DateTime CollectedAt { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;
}
