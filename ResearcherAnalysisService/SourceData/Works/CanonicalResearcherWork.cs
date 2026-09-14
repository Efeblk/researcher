using System.Text.Json.Serialization;
using ResearcherAnalysisService.SourceData.Researchers;

namespace ResearcherAnalysisService.SourceData.Works;

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
