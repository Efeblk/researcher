using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;

public sealed class TrDizinProject
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int TrDizinProfileId { get; set; }

    [JsonIgnore]
    public TrDizinProfile? TrDizinProfile { get; set; } = null;

    public string ProjectId { get; set; } = string.Empty;
    public string? ProjectNumber { get; set; } = null;
    public string? Title { get; set; } = null;
    public string? StartedDate { get; set; } = null;
    public string? EndDate { get; set; } = null;
    public string? ProjectGroup { get; set; } = null;
    public string? ResearchersJson { get; set; } = null;
    public string? Duty { get; set; } = null;
    public string? AbstractsJson { get; set; } = null;
    public string? KeywordsJson { get; set; } = null;
    public string? OutputsJson { get; set; } = null;
    public string? AttachmentsJson { get; set; } = null;

    [JsonIgnore]
    public string RawDataJson { get; set; } = string.Empty;
}
