using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherRandomResponse : ServiceResponse
{
    public ResearcherSummary? Researcher { get; set; } = null;
}

public sealed class ResearcherSummary
{
    public int Id { get; set; }
    public string? UniversityPersonnelId { get; set; } = null;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;
    public string? ScopusAuthorId { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;
    public OpenAlexSummary? OpenAlex { get; set; } = null;
    public GoogleScholarSummary? GoogleScholar { get; set; } = null;
    public ScopusSummary? Scopus { get; set; } = null;
    public WebOfScienceSummary? WebOfScience { get; set; } = null;
}

public sealed class OpenAlexSummary
{
    public string? AuthorId { get; set; } = null;
    public string? DisplayName { get; set; } = null;
    public int WorkCount { get; set; }
    public DateTime? LastUpdatedAt { get; set; } = null;
}

public sealed class GoogleScholarSummary
{
    public string? ScholarId { get; set; } = null;
    public string? Name { get; set; } = null;
    public string? Affiliations { get; set; } = null;
    public int WorkCount { get; set; }
    public int CitationCount { get; set; }
    public int HIndex { get; set; }
    public int I10Index { get; set; }
    public DateTime? LastUpdatedAt { get; set; } = null;
}

public sealed class ScopusSummary
{
    public string? AuthorId { get; set; } = null;
    public string? GivenName { get; set; } = null;
    public string? Surname { get; set; } = null;
    public string? AffiliationName { get; set; } = null;
    public int? DocumentCount { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;
}

public sealed class WebOfScienceSummary
{
    public string? ResearcherId { get; set; } = null;
    public string? FullName { get; set; } = null;
    public string? PrimaryAffiliation { get; set; } = null;
    public int? DocumentCount { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;
}
