using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class SemanticScholarEndpoint : ServiceEndpoint
{
    [HttpPost]
    public async Task<ActionResult<SemanticScholarCollectResponse>> CollectSemanticScholar(
        [FromBody] SemanticScholarCollectRequest request,
        [FromServices] SemanticScholarEnrichmentService enrichmentService,
        [FromServices] AcademicDbContext dbContext, CancellationToken cancellationToken)
    {
        string personelId = request.PersonelId.Trim();
        if (!ModelState.IsValid || personelId.Length is 0 or > 200) return BadRequest(ModelState);
        if (!await dbContext.Researchers.AsNoTracking().AnyAsync(x => x.PersonelId == personelId, cancellationToken)) return NotFound(new { Message = "Researcher was not found." });
        try
        {
            int count = await enrichmentService.EnrichAsync(personelId, cancellationToken);
            return Ok(new SemanticScholarCollectResponse { ProcessedDoiCount = count });
        }
        catch (SemanticScholarPartialEnrichmentException exception)
        {
            return Ok(new SemanticScholarCollectResponse { ProcessedDoiCount = exception.CompletedCount,
                HasPendingWork = true, Message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<SemanticScholarResponse>> ListSemanticScholarPapers(
        [FromBody] SemanticScholarRequest request, [FromServices] AcademicDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        string personelId = request.PersonelId.Trim();
        if (personelId.Length is 0 or > 200 || request.Skip < 0 || request.AcademicWorkId is <= 0) return BadRequest();
        var works = await dbContext.AcademicWorks.AsNoTracking()
            .Where(x => x.PersonelId == personelId && x.Doi != null &&
                (request.AcademicWorkId == null || x.Id == request.AcademicWorkId))
            .Select(x => new { x.Id, x.Doi }).ToListAsync(cancellationToken);
        var linked = works.Select(x => new { x.Id, Doi = SemanticScholarClient.NormalizeDoi(x.Doi) })
            .Where(x => x.Doi.Length > 0).DistinctBy(x => x.Doi).ToList();
        List<string> dois = linked.Select(x => x.Doi).ToList();
        IQueryable<SemanticScholarPaper> query = dbContext.SemanticScholarPapers.AsNoTracking()
            .Where(x => dois.Contains(x.NormalizedDoi));
        int total = await query.CountAsync(cancellationToken);
        List<SemanticScholarPaper> papers = await query.OrderBy(x => x.NormalizedDoi)
            .Skip(Math.Max(0, request.Skip)).Take(request.Take).ToListAsync(cancellationToken);
        SemanticScholarResponse result = new() { TotalPapers = total };
        foreach (SemanticScholarPaper paper in papers)
        {
            int workId = linked.First(x => x.Doi == paper.NormalizedDoi).Id;
            result.Papers.Add(Map(paper, workId));
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<SemanticScholarPaperDto>> ListSemanticScholarCitations(
        [FromBody] SemanticScholarCitationRequest request, [FromServices] AcademicDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.Skip < 0 || string.IsNullOrWhiteSpace(request.PersonelId) || request.PersonelId.Trim().Length > 200)
            return BadRequest(ModelState);
        var work = await dbContext.AcademicWorks.AsNoTracking().Where(x => x.Id == request.AcademicWorkId &&
            x.PersonelId == request.PersonelId.Trim() && x.Doi != null).Select(x => new { x.Id, x.Doi }).SingleOrDefaultAsync(cancellationToken);
        if (work is null) return NotFound(new { Message = "Work was not found for this researcher." });
        string doi = SemanticScholarClient.NormalizeDoi(work.Doi);
        SemanticScholarPaper? paper = await dbContext.SemanticScholarPapers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedDoi == doi, cancellationToken);
        if (paper is null) return NotFound(new { Message = "No saved Semantic Scholar paper exists for this work." });
        SemanticScholarPaperDto result = Map(paper, work.Id);
        List<SemanticScholarCitation> citations = await dbContext.SemanticScholarCitations.AsNoTracking()
            .Include(x => x.Contexts).Where(x => x.TargetPaperId == paper.Id)
            .OrderBy(x => x.Id).Skip(request.Skip).Take(request.Take).ToListAsync(cancellationToken);
        result.Citations = citations.Select(c => new SemanticScholarCitationDto
            {
                CitingPaperId = c.CitingPaperId, CitingDoi = c.CitingDoi, CitingTitle = c.CitingTitle,
                CitingAuthorsJson = c.CitingAuthorsJson, IsInfluential = c.IsInfluential, IntentsJson = c.IntentsJson,
                Contexts = c.Contexts.OrderBy(v => v.Ordinal).Select(v => new SemanticScholarContextDto { Context = v.Context, IntentsJson = v.IntentsJson }).ToList()
            }).ToList();
        return Ok(result);
    }

    private static SemanticScholarPaperDto Map(SemanticScholarPaper x, int workId) => new()
    {
        AcademicWorkId = workId, Doi = x.NormalizedDoi, PaperId = x.PaperId, Found = x.Found, FetchedAt = x.FetchedAt,
        CitationTotal = x.CitationTotal, CitationsFetched = x.CitationsFetched, CitationsComplete = x.CitationsComplete,
        Title = x.Title, Abstract = x.Abstract, AuthorsJson = x.AuthorsJson, Year = x.Year, Venue = x.Venue,
        PublicationDate = x.PublicationDate, JournalJson = x.JournalJson, PublicationTypesJson = x.PublicationTypesJson, FieldsOfStudyJson = x.FieldsOfStudyJson,
        OpenAccessPdfJson = x.OpenAccessPdfJson, CitationCount = x.CitationCount, ReferenceCount = x.ReferenceCount,
        InfluentialCitationCount = x.InfluentialCitationCount, Url = x.Url, TldrJson = x.TldrJson,
        TextAvailability = x.TextAvailability
    };
}
