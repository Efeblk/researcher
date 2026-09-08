namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ResearcherProviderInput
{
    public string? Orcid { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? ScopusId { get; set; } = null;
}

public sealed record ResearcherProviderInputNormalizationResult(
    ResearcherProviderInput Input, List<string> Warnings, string? RejectionReason);
