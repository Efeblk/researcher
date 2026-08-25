using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works;

public sealed class PublicationSummary
{
    public int Id { get; set; }
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonIgnore]
    public string Fingerprint { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string? Doi { get; set; } = null;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkCategory Category { get; set; } = AcademicWorkCategory.Unknown;

    public string? Authors { get; set; } = null;
    public string? Abstract { get; set; } = null;
    public string? Keywords { get; set; } = null;
    public string? Topics { get; set; } = null;
    public string? Language { get; set; } = null;
    public string? Publication { get; set; } = null;
    public string? Volume { get; set; } = null;
    public string? Issue { get; set; } = null;
    public string? FirstPage { get; set; } = null;
    public string? LastPage { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public bool? IsOpenAccess { get; set; } = null;
    public bool? IsRetracted { get; set; } = null;
    public string? PublicationUrl { get; set; } = null;
    public string? PdfUrl { get; set; } = null;
    public string Sources { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    [NotMapped]
    public bool IsApprovedForDisplay { get; set; }

    [JsonIgnore]
    public PublicationDisplayApproval? DisplayApproval { get; set; } = null;
}
