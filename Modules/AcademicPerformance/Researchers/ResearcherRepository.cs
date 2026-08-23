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
            .Include(researcher => researcher.Metrics)
            .Include(researcher => researcher.OrcidProfile)
                .ThenInclude(profile => profile!.Works);

        return query;
    }

    private void UpdateResearcher(Researcher target, Researcher source)
    {
        ApplyRequestValues(target, source);
        target.LastUpdatedAt = DateTime.UtcNow;

        UpdateOrcid(target, source);
        UpdateMetrics(target, source);
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

    private static void UpdateMetrics(Researcher target, Researcher source)
    {
        if (source.Metrics is null)
        {
            return;
        }

        if (target.Metrics is null)
        {
            target.Metrics = source.Metrics;
            return;
        }

        target.Metrics.WorksCount = source.Metrics.WorksCount;
        target.Metrics.CitedByCount = source.Metrics.CitedByCount;
        target.Metrics.HIndex = source.Metrics.HIndex;
        target.Metrics.I10Index = source.Metrics.I10Index;
        target.Metrics.Source = source.Metrics.Source;
        target.Metrics.UpdatedAt = source.Metrics.UpdatedAt;
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
