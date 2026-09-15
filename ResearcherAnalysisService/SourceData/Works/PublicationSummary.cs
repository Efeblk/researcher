using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using ResearcherAnalysisService.SourceData.Researchers;

namespace ResearcherAnalysisService.SourceData.Works;

public sealed class PublicationSummary
{
    public int Id { get; set; }
    public string PersonelId { get; set; } = string.Empty;
    public int? CanonicalWorkId { get; set; } = null;

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonIgnore]
    public string Fingerprint { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public int? PublicationYear { get; set; } = null;
    public string? Doi { get; set; } = null;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkCategory Category { get; set; } = AcademicWorkCategory.Unknown;

    public string? Authors { get; set; } = null;
    public string? Publication { get; set; } = null;
    public string? PublicationUrl { get; set; } = null;
    public string Sources { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    [NotMapped]
    public bool IsApprovedForDisplay { get; set; }

    [JsonIgnore]
    public PublicationDisplayApproval? DisplayApproval { get; set; } = null;
}
