using Microsoft.EntityFrameworkCore;

public sealed class ResearcherRepository
{
    private readonly AcademicDbContext _dbContext;

    public ResearcherRepository(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveAsync(Researcher researcher)
    {
        Researcher? existingResearcher = null;

        if (!string.IsNullOrWhiteSpace(researcher.Orcid))
        {
            existingResearcher = await CreateResearcherQuery()
                .FirstOrDefaultAsync(item => item.Orcid == researcher.Orcid);
        }

        if (existingResearcher is null && !string.IsNullOrWhiteSpace(researcher.GoogleScholarId))
        {
            existingResearcher = await CreateResearcherQuery()
                .FirstOrDefaultAsync(item => item.GoogleScholarId == researcher.GoogleScholarId);
        }

        if (existingResearcher is null)
        {
            researcher.LastUpdatedAt = DateTime.UtcNow;
            _dbContext.Researchers.Add(researcher);
        }
        else
        {
            UpdateResearcher(existingResearcher, researcher);
        }

        await _dbContext.SaveChangesAsync();
        researcher.Id = existingResearcher?.Id ?? researcher.Id;
    }

    private IQueryable<Researcher> CreateResearcherQuery()
    {
        IQueryable<Researcher>? query = null;

        query = _dbContext.Researchers
            .Include(researcher => researcher.OpenAlex)
                .ThenInclude(openAlex => openAlex!.Works)
            .Include(researcher => researcher.GoogleScholar)
                .ThenInclude(googleScholar => googleScholar!.Works)
            .Include(researcher => researcher.GoogleScholar)
                .ThenInclude(googleScholar => googleScholar!.Interests);

        return query;
    }

    private void UpdateResearcher(Researcher target, Researcher source)
    {
        target.UniversityPersonnelId = source.UniversityPersonnelId ?? target.UniversityPersonnelId;
        target.FirstName = source.FirstName ?? target.FirstName;
        target.LastName = source.LastName ?? target.LastName;
        target.AcademicTitle = source.AcademicTitle ?? target.AcademicTitle;
        target.Department = source.Department ?? target.Department;
        target.WebOfScienceResearcherId = source.WebOfScienceResearcherId ?? target.WebOfScienceResearcherId;
        target.ScopusAuthorId = source.ScopusAuthorId ?? target.ScopusAuthorId;
        target.Orcid = source.Orcid ?? target.Orcid;
        target.GoogleScholarId = source.GoogleScholarId ?? target.GoogleScholarId;
        target.LastUpdatedAt = DateTime.UtcNow;

        UpdateOpenAlex(target, source);
        UpdateGoogleScholar(target, source);
    }

    private void UpdateOpenAlex(Researcher target, Researcher source)
    {
        if (source.OpenAlex is null)
        {
            return;
        }

        if (target.OpenAlex is null)
        {
            target.OpenAlex = source.OpenAlex;
            return;
        }

        target.OpenAlex.AuthorId = source.OpenAlex.AuthorId;
        target.OpenAlex.DisplayName = source.OpenAlex.DisplayName;
        target.OpenAlex.WorksCount = source.OpenAlex.WorksCount;

        if (source.OpenAlex.Works is not null)
        {
            _dbContext.OpenAlexWorks.RemoveRange(target.OpenAlex.Works ?? []);
            target.OpenAlex.Works = source.OpenAlex.Works;
        }
    }

    private void UpdateGoogleScholar(Researcher target, Researcher source)
    {
        if (source.GoogleScholar is null)
        {
            return;
        }

        if (target.GoogleScholar is null)
        {
            target.GoogleScholar = source.GoogleScholar;
            return;
        }

        target.GoogleScholar.ScholarId = source.GoogleScholar.ScholarId;
        target.GoogleScholar.Name = source.GoogleScholar.Name;
        target.GoogleScholar.Affiliations = source.GoogleScholar.Affiliations;
        target.GoogleScholar.Email = source.GoogleScholar.Email;

        if (source.GoogleScholar.Works is not null)
        {
            _dbContext.GoogleScholarWorks.RemoveRange(target.GoogleScholar.Works ?? []);
            target.GoogleScholar.Works = source.GoogleScholar.Works;
        }

        if (source.GoogleScholar.Interests is not null)
        {
            _dbContext.GoogleScholarInterests.RemoveRange(target.GoogleScholar.Interests ?? []);
            target.GoogleScholar.Interests = source.GoogleScholar.Interests;
        }
    }
}
