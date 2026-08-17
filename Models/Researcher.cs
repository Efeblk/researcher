public sealed class Researcher
{
    public int Id { get; set; }

    public string? UniversityPersonnelId { get; set; } = null;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;

    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? ScopusAuthorId { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;

    public string? OpenAlexAuthorId { get; set; } = null;
}
