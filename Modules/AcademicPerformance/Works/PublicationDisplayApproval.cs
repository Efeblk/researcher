using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works;

public sealed class PublicationDisplayApproval
{
    public int Id { get; set; }
    public int ResearcherId { get; set; }
    public int PublicationSummaryId { get; set; }
    public DateTime ApprovedAt { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonIgnore]
    public PublicationSummary? PublicationSummary { get; set; } = null;
}
