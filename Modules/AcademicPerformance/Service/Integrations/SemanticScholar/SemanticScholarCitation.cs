namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed class SemanticScholarCitation
{
    public int Id { get; set; }
    public int TargetPaperId { get; set; }
    public SemanticScholarPaper TargetPaper { get; set; } = null!;
    public string CitingPaperId { get; set; } = string.Empty;
    public string? CitingDoi { get; set; } = null;
    public string? CitingTitle { get; set; } = null;
    public string? CitingAuthorsJson { get; set; } = null;
    public bool? IsInfluential { get; set; } = null;
    public string? IntentsJson { get; set; } = null;
    public string? RawDataJson { get; set; } = null;
    public List<SemanticScholarCitationContext> Contexts { get; set; } = [];
}

public sealed class SemanticScholarCitationContext
{
    public int Id { get; set; }
    public int CitationId { get; set; }
    public SemanticScholarCitation Citation { get; set; } = null!;
    public int Ordinal { get; set; }
    public string Context { get; set; } = string.Empty;
    public string? IntentsJson { get; set; } = null;
}
