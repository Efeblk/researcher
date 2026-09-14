using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Pages.AcademicPerformance;

public sealed class AcademicPerformancePage : Controller
{
    [HttpGet("AcademicPerformance")]
    public IActionResult Index()
    {
        return View(
            "~/Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/Index.cshtml");
    }

    [HttpPost("Services/AcademicPerformance/WebClient/ResolvePersonelId")]
    public async Task<ResolvePersonelIdResponse> ResolvePersonelId(
        [FromBody] ResolvePersonelIdRequest request,
        [FromServices] AcademicDbContext dbContext)
    {
        string? orcid = Normalize(request.Orcid, ResearcherIdentifierParser.NormalizeOrcid);
        string? scholarId = Normalize(
            request.GoogleScholarId,
            ResearcherIdentifierParser.NormalizeGoogleScholarId);
        string? researcherId = Normalize(
            request.WebOfScienceResearcherId,
            ResearcherIdentifierParser.NormalizeResearcherId);
        string? tcKimlikNo = string.IsNullOrWhiteSpace(request.TcKimlikNo)
            ? null
            : request.TcKimlikNo.Trim();

        if (orcid is null && scholarId is null && researcherId is null && tcKimlikNo is null)
            throw new ArgumentException("En az bir akademisyen kimliği verilmelidir.");

        List<string> matchingPersonelIds = await dbContext.Researchers
            .AsNoTracking()
            .Where(researcher =>
                (orcid != null && researcher.Orcid == orcid) ||
                (scholarId != null && researcher.GoogleScholarId == scholarId) ||
                (researcherId != null &&
                    researcher.WebOfScienceResearcherId == researcherId) ||
                (tcKimlikNo != null && researcher.TcKimlikNo == tcKimlikNo))
            .Select(researcher => researcher.PersonelId)
            .Distinct()
            .Take(2)
            .ToListAsync();

        if (matchingPersonelIds.Count > 1)
            throw new ArgumentException("Kimlikler farklı akademisyen kayıtlarıyla eşleşiyor.");

        return new ResolvePersonelIdResponse
        {
            PersonelId = matchingPersonelIds.SingleOrDefault() ?? $"web-{Guid.NewGuid():N}"
        };
    }

    private static string? Normalize(string? value, Func<string, string> normalize)
    {
        return string.IsNullOrWhiteSpace(value) ? null : normalize(value);
    }
}

public sealed class ResolvePersonelIdRequest
{
    [JsonPropertyName("ORCID"), Newtonsoft.Json.JsonProperty("ORCID")]
    public string? Orcid { get; set; } = null;
    [JsonPropertyName("ScholarID"), Newtonsoft.Json.JsonProperty("ScholarID")]
    public string? GoogleScholarId { get; set; } = null;
    [JsonPropertyName("ResearcherID"), Newtonsoft.Json.JsonProperty("ResearcherID")]
    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? TcKimlikNo { get; set; } = null;
}

public sealed class ResolvePersonelIdResponse
{
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
}
