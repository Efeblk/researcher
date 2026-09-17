using System.Text.Json;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ResearcherProviderDetailsDto
{
    public OrcidResearcherDetailsDto? Orcid { get; set; } = null;
    public OpenAlexResearcherDetailsDto? OpenAlex { get; set; } = null;
    public ScopusResearcherDetailsDto? Scopus { get; set; } = null;
    public GoogleScholarResearcherDetailsDto? GoogleScholar { get; set; } = null;
}

public sealed class OrcidResearcherDetailsDto
{
    public JsonElement? Activities { get; set; } = null;
    public JsonElement? OtherNames { get; set; } = null;
    public JsonElement? Emails { get; set; } = null;
}

public sealed class OpenAlexResearcherDetailsDto
{
    public JsonElement? ExternalIdentifiers { get; set; } = null;
    public JsonElement? AlternativeNames { get; set; } = null;
    public JsonElement? RawAuthorNames { get; set; } = null;
    public JsonElement? Affiliations { get; set; } = null;
    public JsonElement? LastKnownInstitutions { get; set; } = null;
    public JsonElement? Topics { get; set; } = null;
    public JsonElement? TopicShare { get; set; } = null;
}

public sealed class ScopusResearcherDetailsDto
{
    public string? Orcid { get; set; } = null;
    public JsonElement? NameVariants { get; set; } = null;
    public JsonElement? CurrentAffiliation { get; set; } = null;
    public JsonElement? AffiliationHistory { get; set; } = null;
    public JsonElement? SubjectAreas { get; set; } = null;
    public int? CoauthorCount { get; set; } = null;
}

public sealed class GoogleScholarResearcherDetailsDto
{
    public JsonElement? Interests { get; set; } = null;
    public JsonElement? CoAuthors { get; set; } = null;
    public JsonElement? PublicAccess { get; set; } = null;
    public string? Thumbnail { get; set; } = null;
}
