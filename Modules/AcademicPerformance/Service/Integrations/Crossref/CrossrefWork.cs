namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;

public sealed class CrossrefWork
{
    public int Id { get; set; }
    public string PersonelId { get; set; } = string.Empty;
    public string Doi { get; set; } = string.Empty;
    public bool Found { get; set; }
    public DateTime FetchedAt { get; set; }
    public string? Title { get; set; } = null;
    public string? Authors { get; set; } = null;
    public string? ContainerTitle { get; set; } = null;
    public string? Type { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public string? Url { get; set; } = null;
    public string? RawDataJson { get; set; } = null;
}
