namespace ResearcherAnalysisService.SourceData.Works;

public sealed class CanonicalWork
{
    public int Id { get; set; }
    public string? NormalizedDoi { get; set; } = null;
    public string? SourceScopedKey { get; set; } = null;
    public bool HasRetractionObservation { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<CanonicalWorkObservation> Observations { get; set; } = [];
    public List<CanonicalResearcherWork> Researchers { get; set; } = [];
}
