namespace ResearcherAnalysisService.SourceData;

public sealed class CollectionChange
{
    public Guid EventId { get; set; }
    public string ChangeKind { get; set; } = string.Empty;
    public string? PersonelId { get; set; }
    public int? CanonicalWorkId { get; set; }
    public int? AcademicWorkId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public int PayloadVersion { get; set; }
}
