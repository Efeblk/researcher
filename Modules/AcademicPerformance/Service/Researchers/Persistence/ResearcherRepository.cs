using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;

public sealed class ResearcherRepository
{
    private readonly AcademicDbContext _dbContext;

    public ResearcherRepository(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Researcher?> FindByIdentifiersAsync(Researcher identifiers)
    {
        List<string> matchingPersonelIds = await _dbContext.Researchers
            .Where(item =>
                (identifiers.PersonelId != null && item.PersonelId == identifiers.PersonelId) ||
                (identifiers.Orcid != null && item.Orcid == identifiers.Orcid) ||
                (identifiers.GoogleScholarId != null && item.GoogleScholarId == identifiers.GoogleScholarId) ||
                (identifiers.WebOfScienceResearcherId != null && item.WebOfScienceResearcherId == identifiers.WebOfScienceResearcherId) ||
                (identifiers.TcKimlikNo != null && item.TcKimlikNo == identifiers.TcKimlikNo))
            .Select(item => item.PersonelId)
            .Take(2)
            .ToListAsync();

        if (matchingPersonelIds.Count > 1)
            throw new ArgumentException("Sağlayıcı kimlikleri farklı akademisyen kayıtlarına ait.");

        return matchingPersonelIds.Count == 0
            ? null
            : await FindByPersonelIdAsync(matchingPersonelIds[0]);
    }

    public Task<Researcher?> FindByPersonelIdAsync(string personelId)
    {
        return CreateResearcherQuery()
            .FirstOrDefaultAsync(researcher => researcher.PersonelId == personelId);
    }

    public void ApplyRequestValues(Researcher target, Researcher source)
    {
        if (!target.PersonelId.Equals(source.PersonelId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PersonelID mevcut akademisyen kaydıyla eşleşmiyor.");
        target.FirstName = source.FirstName ?? target.FirstName;
        target.LastName = source.LastName ?? target.LastName;
        target.AcademicTitle = source.AcademicTitle ?? target.AcademicTitle;
        target.Department = source.Department ?? target.Department;

        target.Orcid = GetIdentifierValue(target.Orcid, source.Orcid, "ORCID");
        // Scopus is stored as source metadata only; it is not used for matching or collection.
        target.ScopusId ??= source.ScopusId;
        target.GoogleScholarId = GetIdentifierValue(
            target.GoogleScholarId,
            source.GoogleScholarId,
            "Google Scholar ID");
        target.WebOfScienceResearcherId = GetIdentifierValue(
            target.WebOfScienceResearcherId,
            source.WebOfScienceResearcherId,
            "Web of Science ResearcherID");
        target.TcKimlikNo = GetIdentifierValue(
            target.TcKimlikNo,
            source.TcKimlikNo,
            "T.C. Kimlik No");
    }

    public async Task SaveAsync(Researcher researcher)
    {
        if (_dbContext.Entry(researcher).State != EntityState.Detached)
        {
            researcher.LastUpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return;
        }

        Researcher? existingResearcher = await FindByIdentifiersAsync(researcher);

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
    }

    private IQueryable<Researcher> CreateResearcherQuery()
    {
        IQueryable<Researcher>? query = _dbContext.Researchers
            .Include(researcher => researcher.OrcidProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.GoogleScholarProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.OpenAlexProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.WebOfScienceProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.WebOfScienceProfile)
                .ThenInclude(profile => profile!.PeerReviews)
            .Include(researcher => researcher.TrDizinProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.AcademicWorks)
            .AsSplitQuery();

        return query;
    }

    private void UpdateResearcher(Researcher target, Researcher source)
    {
        ApplyRequestValues(target, source);
        target.LastUpdatedAt = DateTime.UtcNow;

        UpdateOrcid(target, source);
        UpdateGoogleScholar(target, source);
        UpdateOpenAlex(target, source);
        UpdateWebOfScience(target, source);
        UpdateTrDizin(target, source);
    }

    private void UpdateTrDizin(Researcher target, Researcher source)
    {
        if (source.TrDizinProfile is null) return;
        if (target.TrDizinProfile is null) { target.TrDizinProfile = source.TrDizinProfile; return; }
        TrDizinProfile profile = target.TrDizinProfile;
        TrDizinProfile incoming = source.TrDizinProfile;
        profile.Orcid = incoming.Orcid; profile.AuthorId = incoming.AuthorId;
        profile.DisplayName = incoming.DisplayName ?? profile.DisplayName;
        profile.PublicationCount = incoming.PublicationCount ?? profile.PublicationCount;
        profile.CitationCount = incoming.CitationCount ?? profile.CitationCount;
        profile.LastUpdatedAt = incoming.LastUpdatedAt; profile.RawAuthorJson = incoming.RawAuthorJson;
        profile.RawPublicationsJson = incoming.RawPublicationsJson;
        _dbContext.TrDizinWorks.RemoveRange(profile.Works ?? []); profile.Works = incoming.Works;
    }

    private void UpdateOpenAlex(Researcher target, Researcher source)
    {
        OpenAlexProfile? sourceProfile = source.OpenAlexProfile;

        if (sourceProfile is null)
        {
            return;
        }

        if (target.OpenAlexProfile is null)
        {
            target.OpenAlexProfile = sourceProfile;
            return;
        }

        OpenAlexProfile targetProfile = target.OpenAlexProfile;
        targetProfile.OpenAlexAuthorId = sourceProfile.OpenAlexAuthorId;
        targetProfile.DisplayName = sourceProfile.DisplayName;
        targetProfile.LastKnownInstitution = sourceProfile.LastKnownInstitution;
        targetProfile.WorksCount = sourceProfile.WorksCount;
        targetProfile.CitedByCount = sourceProfile.CitedByCount;
        targetProfile.HIndex = sourceProfile.HIndex;
        targetProfile.I10Index = sourceProfile.I10Index;
        targetProfile.TwoYearMeanCitedness = sourceProfile.TwoYearMeanCitedness;
        targetProfile.LastUpdatedAt = sourceProfile.LastUpdatedAt;
        targetProfile.CountsByYearJson = sourceProfile.CountsByYearJson;
        targetProfile.RawDataJson = sourceProfile.RawDataJson;
        targetProfile.WorksPagesJson = sourceProfile.WorksPagesJson;

        _dbContext.OpenAlexWorks.RemoveRange(targetProfile.Works ?? []);
        targetProfile.Works = sourceProfile.Works;
    }

    private void UpdateGoogleScholar(Researcher target, Researcher source)
    {
        GoogleScholarProfile? sourceProfile = source.GoogleScholarProfile;

        if (sourceProfile is null)
        {
            return;
        }

        if (target.GoogleScholarProfile is null)
        {
            target.GoogleScholarProfile = sourceProfile;
            return;
        }

        GoogleScholarProfile targetProfile = target.GoogleScholarProfile;
        targetProfile.DisplayName = sourceProfile.DisplayName;
        targetProfile.Affiliations = sourceProfile.Affiliations;
        targetProfile.University = sourceProfile.University;
        targetProfile.VerifiedEmail = sourceProfile.VerifiedEmail;
        targetProfile.ProfileUrl = sourceProfile.ProfileUrl;
        targetProfile.CitationCount = sourceProfile.CitationCount;
        targetProfile.CitationCountRecent = sourceProfile.CitationCountRecent;
        targetProfile.HIndex = sourceProfile.HIndex;
        targetProfile.HIndexRecent = sourceProfile.HIndexRecent;
        targetProfile.I10Index = sourceProfile.I10Index;
        targetProfile.I10IndexRecent = sourceProfile.I10IndexRecent;
        targetProfile.MetricsSinceYear = sourceProfile.MetricsSinceYear;
        targetProfile.DocumentsCount = sourceProfile.DocumentsCount;
        targetProfile.LastUpdatedAt = sourceProfile.LastUpdatedAt;
        targetProfile.InterestsJson = sourceProfile.InterestsJson;
        targetProfile.CitationHistogramJson = sourceProfile.CitationHistogramJson;
        targetProfile.RawDataJson = sourceProfile.RawDataJson;

        _dbContext.GoogleScholarWorks.RemoveRange(targetProfile.Works ?? []);
        targetProfile.Works = sourceProfile.Works;
    }

    private void UpdateWebOfScience(Researcher target, Researcher source)
    {
        WebOfScienceProfile? sourceProfile = source.WebOfScienceProfile;

        if (sourceProfile is null)
        {
            return;
        }

        if (target.WebOfScienceProfile is null)
        {
            target.WebOfScienceProfile = sourceProfile;
            return;
        }

        WebOfScienceProfile? targetProfile = target.WebOfScienceProfile;
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
