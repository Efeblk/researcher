using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherRepository
{
    private readonly AcademicDbContext _dbContext;

    public ResearcherRepository(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Researcher?> FindByIdentifiersAsync(Researcher identifiers)
    {
        Researcher? researcher = null;

        if (!string.IsNullOrWhiteSpace(identifiers.Orcid))
        {
            researcher = await CreateResearcherQuery()
                .FirstOrDefaultAsync(item => item.Orcid == identifiers.Orcid);
        }

        if (researcher is null && !string.IsNullOrWhiteSpace(identifiers.GoogleScholarId))
        {
            researcher = await CreateResearcherQuery()
                .FirstOrDefaultAsync(
                    item => item.GoogleScholarId == identifiers.GoogleScholarId);
        }

        if (researcher is null &&
            !string.IsNullOrWhiteSpace(identifiers.WebOfScienceResearcherId))
        {
            researcher = await CreateResearcherQuery()
                .FirstOrDefaultAsync(
                    item => item.WebOfScienceResearcherId ==
                            identifiers.WebOfScienceResearcherId);
        }

        return researcher;
    }

    public void ApplyRequestValues(Researcher target, Researcher source)
    {
        target.UniversityPersonnelId = source.UniversityPersonnelId ?? target.UniversityPersonnelId;
        target.FirstName = source.FirstName ?? target.FirstName;
        target.LastName = source.LastName ?? target.LastName;
        target.AcademicTitle = source.AcademicTitle ?? target.AcademicTitle;
        target.Department = source.Department ?? target.Department;

        target.Orcid = GetIdentifierValue(target.Orcid, source.Orcid, "ORCID");
        target.GoogleScholarId = GetIdentifierValue(
            target.GoogleScholarId,
            source.GoogleScholarId,
            "Google Scholar ID");
        target.WebOfScienceResearcherId = GetIdentifierValue(
            target.WebOfScienceResearcherId,
            source.WebOfScienceResearcherId,
            "Web of Science ResearcherID");
    }

    public async Task SaveAsync(Researcher researcher)
    {
        Researcher? existingResearcher = null;

        if (researcher.Id > 0)
        {
            researcher.LastUpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return;
        }

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

        if (existingResearcher is null &&
            !string.IsNullOrWhiteSpace(researcher.WebOfScienceResearcherId))
        {
            existingResearcher = await CreateResearcherQuery()
                .FirstOrDefaultAsync(
                    item => item.WebOfScienceResearcherId == researcher.WebOfScienceResearcherId);
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
                .ThenInclude(googleScholar => googleScholar!.Interests)
            .Include(researcher => researcher.WebOfScience);

        return query;
    }

    private void UpdateResearcher(Researcher target, Researcher source)
    {
        ApplyRequestValues(target, source);
        target.LastUpdatedAt = DateTime.UtcNow;

        UpdateOpenAlex(target, source);
        UpdateGoogleScholar(target, source);
        UpdateWebOfScience(target, source);
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
        target.OpenAlex.RawDataJson = source.OpenAlex.RawDataJson;
        target.OpenAlex.WorksResponsePagesJson = source.OpenAlex.WorksResponsePagesJson;
        target.OpenAlex.LastUpdatedAt = source.OpenAlex.LastUpdatedAt;

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
        target.GoogleScholar.CitationCount = source.GoogleScholar.CitationCount;
        target.GoogleScholar.HIndex = source.GoogleScholar.HIndex;
        target.GoogleScholar.I10Index = source.GoogleScholar.I10Index;
        target.GoogleScholar.RawDataJson = source.GoogleScholar.RawDataJson;
        target.GoogleScholar.ResponsePagesJson = source.GoogleScholar.ResponsePagesJson;
        target.GoogleScholar.LastUpdatedAt = source.GoogleScholar.LastUpdatedAt;

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

    private void UpdateWebOfScience(Researcher target, Researcher source)
    {
        if (source.WebOfScience is null)
        {
            return;
        }

        if (target.WebOfScience is null)
        {
            target.WebOfScience = source.WebOfScience;
            return;
        }

        target.WebOfScience.Rid = source.WebOfScience.Rid;
        target.WebOfScience.FullName = source.WebOfScience.FullName;
        target.WebOfScience.FirstName = source.WebOfScience.FirstName;
        target.WebOfScience.LastName = source.WebOfScience.LastName;
        target.WebOfScience.PrimaryAffiliation = source.WebOfScience.PrimaryAffiliation;
        target.WebOfScience.Address = source.WebOfScience.Address;
        target.WebOfScience.Country = source.WebOfScience.Country;
        target.WebOfScience.IsClaimed = source.WebOfScience.IsClaimed;
        target.WebOfScience.DocumentCount = source.WebOfScience.DocumentCount;
        target.WebOfScience.TotalTimesCited = source.WebOfScience.TotalTimesCited;
        target.WebOfScience.TotalCitingPublications = source
            .WebOfScience
            .TotalCitingPublications;
        target.WebOfScience.HIndex = source.WebOfScience.HIndex;
        target.WebOfScience.LastUpdatedAt = source.WebOfScience.LastUpdatedAt;
    }

    private static string? GetIdentifierValue(
        string? currentValue,
        string? requestedValue,
        string identifierName)
    {
        if (string.IsNullOrWhiteSpace(requestedValue))
        {
            return currentValue;
        }

        if (string.IsNullOrWhiteSpace(currentValue))
        {
            return requestedValue;
        }

        if (!currentValue.Equals(requestedValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{identifierName} mevcut akademisyen kaydındaki değerle eşleşmiyor.");
        }

        return currentValue;
    }
}
