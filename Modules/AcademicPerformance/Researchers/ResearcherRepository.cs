using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

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

        if (researcher is null &&
            !string.IsNullOrWhiteSpace(identifiers.WebOfScienceResearcherId))
        {
            researcher = await CreateResearcherQuery()
                .FirstOrDefaultAsync(item =>
                    item.WebOfScienceResearcherId ==
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

        if (existingResearcher is null &&
            !string.IsNullOrWhiteSpace(researcher.WebOfScienceResearcherId))
        {
            existingResearcher = await CreateResearcherQuery()
                .FirstOrDefaultAsync(item =>
                    item.WebOfScienceResearcherId ==
                    researcher.WebOfScienceResearcherId);
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
            .Include(researcher => researcher.OrcidProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.WebOfScienceProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.WebOfScienceProfile)
                .ThenInclude(profile => profile!.PeerReviews);

        return query;
    }

    private void UpdateResearcher(Researcher target, Researcher source)
    {
        ApplyRequestValues(target, source);
        target.LastUpdatedAt = DateTime.UtcNow;

        UpdateOrcid(target, source);
        UpdateWebOfScience(target, source);
    }

    private void UpdateWebOfScience(Researcher target, Researcher source)
    {
        WebOfScienceProfile? targetProfile = null;
        WebOfScienceProfile? sourceProfile = null;

        sourceProfile = source.WebOfScienceProfile;

        if (sourceProfile is null)
        {
            return;
        }

        if (target.WebOfScienceProfile is null)
        {
            target.WebOfScienceProfile = sourceProfile;
            return;
        }

        targetProfile = target.WebOfScienceProfile;
        targetProfile.DisplayName = sourceProfile.DisplayName;
        targetProfile.FirstName = sourceProfile.FirstName;
        targetProfile.LastName = sourceProfile.LastName;
        targetProfile.Orcid = sourceProfile.Orcid;
        targetProfile.IsClaimed = sourceProfile.IsClaimed;
        targetProfile.PrimaryOrganization = sourceProfile.PrimaryOrganization;
        targetProfile.PrimaryAddress = sourceProfile.PrimaryAddress;
        targetProfile.PrimaryCountry = sourceProfile.PrimaryCountry;
        targetProfile.Departments = sourceProfile.Departments;
        targetProfile.HIndex = sourceProfile.HIndex;
        targetProfile.DocumentsCount = sourceProfile.DocumentsCount;
        targetProfile.TotalCitingPublications = sourceProfile.TotalCitingPublications;
        targetProfile.TotalCitingWithoutSelf = sourceProfile.TotalCitingWithoutSelf;
        targetProfile.TotalTimesCited = sourceProfile.TotalTimesCited;
        targetProfile.TotalTimesCitedWithoutSelf = sourceProfile.TotalTimesCitedWithoutSelf;
        targetProfile.PeerReviewsCount = sourceProfile.PeerReviewsCount;
        targetProfile.LastUpdatedAt = sourceProfile.LastUpdatedAt;
        targetProfile.AlternativeNamesJson = sourceProfile.AlternativeNamesJson;
        targetProfile.AffiliationsJson = sourceProfile.AffiliationsJson;
        targetProfile.AuthorPositionsJson = sourceProfile.AuthorPositionsJson;
        targetProfile.SubjectCategoriesJson = sourceProfile.SubjectCategoriesJson;
        targetProfile.AwardsJson = sourceProfile.AwardsJson;
        targetProfile.RawDataJson = sourceProfile.RawDataJson;
        targetProfile.DocumentPagesJson = sourceProfile.DocumentPagesJson;
        targetProfile.PeerReviewPagesJson = sourceProfile.PeerReviewPagesJson;

        _dbContext.WebOfScienceWorks.RemoveRange(targetProfile.Works ?? []);
        _dbContext.WebOfSciencePeerReviews.RemoveRange(
            targetProfile.PeerReviews ?? []);
        targetProfile.Works = sourceProfile.Works;
        targetProfile.PeerReviews = sourceProfile.PeerReviews;
    }

    private void UpdateOrcid(Researcher target, Researcher source)
    {
        if (source.OrcidProfile is null)
        {
            return;
        }

        if (target.OrcidProfile is null)
        {
            target.OrcidProfile = source.OrcidProfile;
            return;
        }

        target.OrcidProfile.DisplayName = source.OrcidProfile.DisplayName;
        target.OrcidProfile.GivenNames = source.OrcidProfile.GivenNames;
        target.OrcidProfile.FamilyName = source.OrcidProfile.FamilyName;
        target.OrcidProfile.CreditName = source.OrcidProfile.CreditName;
        target.OrcidProfile.Biography = source.OrcidProfile.Biography;
        target.OrcidProfile.CountryCodes = source.OrcidProfile.CountryCodes;
        target.OrcidProfile.Keywords = source.OrcidProfile.Keywords;
        target.OrcidProfile.CurrentOrganization = source.OrcidProfile.CurrentOrganization;
        target.OrcidProfile.CurrentDepartment = source.OrcidProfile.CurrentDepartment;
        target.OrcidProfile.CurrentRoleTitle = source.OrcidProfile.CurrentRoleTitle;
        target.OrcidProfile.WorksCount = source.OrcidProfile.WorksCount;
        target.OrcidProfile.EmploymentsCount = source.OrcidProfile.EmploymentsCount;
        target.OrcidProfile.EducationsCount = source.OrcidProfile.EducationsCount;
        target.OrcidProfile.FundingsCount = source.OrcidProfile.FundingsCount;
        target.OrcidProfile.PeerReviewsCount = source.OrcidProfile.PeerReviewsCount;
        target.OrcidProfile.RecordLastModifiedAt = source.OrcidProfile.RecordLastModifiedAt;
        target.OrcidProfile.LastUpdatedAt = source.OrcidProfile.LastUpdatedAt;
        target.OrcidProfile.ResearcherUrlsJson = source.OrcidProfile.ResearcherUrlsJson;
        target.OrcidProfile.ExternalIdentifiersJson = source.OrcidProfile.ExternalIdentifiersJson;
        target.OrcidProfile.EmploymentsJson = source.OrcidProfile.EmploymentsJson;
        target.OrcidProfile.EducationsJson = source.OrcidProfile.EducationsJson;
        target.OrcidProfile.ActivitiesJson = source.OrcidProfile.ActivitiesJson;
        target.OrcidProfile.RawDataJson = source.OrcidProfile.RawDataJson;

        _dbContext.OrcidWorks.RemoveRange(target.OrcidProfile.Works ?? []);
        target.OrcidProfile.Works = source.OrcidProfile.Works;
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
