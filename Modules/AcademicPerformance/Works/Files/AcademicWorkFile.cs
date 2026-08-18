using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Files;

public sealed class AcademicWorkFile
{
    public int Id { get; set; }
    public int AcademicWorkId { get; set; }

    [JsonIgnore]
    public AcademicWork? AcademicWork { get; set; } = null;

    public string? SourceUrl { get; set; } = null;
    public string? RelativePath { get; set; } = null;
    public string? FileName { get; set; } = null;
    public string? MimeType { get; set; } = null;
    public long? FileSizeBytes { get; set; } = null;
    public string? Sha256 { get; set; } = null;
    public DateTime? DownloadedAt { get; set; } = null;
    public DateTime? LastAttemptedAt { get; set; } = null;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkFileStatus Status { get; set; } =
        AcademicWorkFileStatus.Pending;

    public string? ErrorMessage { get; set; } = null;
}

public enum AcademicWorkFileStatus
{
    Pending,
    Downloaded,
    Failed
}
