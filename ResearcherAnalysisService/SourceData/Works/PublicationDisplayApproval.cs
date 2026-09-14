using System.Text.Json.Serialization;
using ResearcherAnalysisService.SourceData.Researchers;

namespace ResearcherAnalysisService.SourceData.Works;

public sealed class PublicationDisplayApproval
{
    public int Id { get; set; }
    public string PersonelId { get; set; } = string.Empty;
    public int PublicationSummaryId { get; set; }
    public DateTime ApprovedAt { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonIgnore]
    public PublicationSummary? PublicationSummary { get; set; } = null;
}
