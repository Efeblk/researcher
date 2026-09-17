using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;

public sealed class ResearcherRepository
{
    private readonly AcademicDbContext _dbContext;
    private readonly ILogger<ResearcherRepository> _logger;

    public ResearcherRepository(
        AcademicDbContext dbContext,
        ILogger<ResearcherRepository>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger ?? NullLogger<ResearcherRepository>.Instance;
    }

    public async Task<Researcher?> FindByIdentifiersAsync(
        Researcher identifiers,
        CancellationToken cancellationToken = default)
    {
        List<string> matchingPersonelIds = await _dbContext.Researchers
            .Where(item =>
                (identifiers.PersonelId != null && item.PersonelId == identifiers.PersonelId) ||
                (identifiers.Orcid != null && item.Orcid == identifiers.Orcid) ||
                (identifiers.GoogleScholarId != null && item.GoogleScholarId == identifiers.GoogleScholarId) ||
                (identifiers.WebOfScienceResearcherId != null && item.WebOfScienceResearcherId == identifiers.WebOfScienceResearcherId) ||
                (identifiers.ScopusId != null && item.ScopusId == identifiers.ScopusId) ||
                (identifiers.TcKimlikNo != null && item.TcKimlikNo == identifiers.TcKimlikNo))
            .Select(item => item.PersonelId)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matchingPersonelIds.Count > 1)
            throw new ArgumentException("Sağlayıcı kimlikleri farklı akademisyen kayıtlarına ait.");

        return matchingPersonelIds.Count == 0
            ? null
            : await FindByPersonelIdAsync(matchingPersonelIds[0], cancellationToken);
    }

    public async Task<Researcher?> FindByPersonelIdAsync(
        string personelId,
        CancellationToken cancellationToken = default)
    {
        LogStageStarted(personelId, "profiles");
        long startedAt = Stopwatch.GetTimestamp();
        Researcher? researcher = await CreateResearcherQuery()
            .FirstOrDefaultAsync(
                researcher => researcher.PersonelId == personelId,
                cancellationToken);

        if (researcher is null)
            return null;

        LogStageLoaded(personelId, "profiles", Stopwatch.GetElapsedTime(startedAt), 1);
        await LoadCollectionsAsync(researcher, cancellationToken);
        return researcher;
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
        target.ScopusId = GetIdentifierValue(target.ScopusId, source.ScopusId, "Scopus ID");
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

    public async Task SaveAsync(
        Researcher researcher,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Entry(researcher).State != EntityState.Detached)
        {
            researcher.LastUpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        Researcher? existingResearcher = await FindByIdentifiersAsync(researcher, cancellationToken);

        if (existingResearcher is null)
        {
            researcher.LastUpdatedAt = DateTime.UtcNow;
            _dbContext.Researchers.Add(researcher);
        }
        else
        {
            UpdateResearcher(existingResearcher, researcher);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Researcher> CreateResearcherQuery()
    {
        IQueryable<Researcher>? query = _dbContext.Researchers
            .Include(researcher => researcher.OrcidProfile)
            .Include(researcher => researcher.GoogleScholarProfile)
            .Include(researcher => researcher.OpenAlexProfile)
            .Include(researcher => researcher.ScopusProfile)
            .Include(researcher => researcher.WebOfScienceProfile)
            .Include(researcher => researcher.TrDizinProfile);

        return query;
    }

    private async Task LoadCollectionsAsync(
        Researcher researcher,
        CancellationToken cancellationToken)
    {
        if (researcher.OrcidProfile is not null)
        {
            LogStageStarted(researcher.PersonelId, "orcid-works");
            long startedAt = Stopwatch.GetTimestamp();
            await _dbContext.Entry(researcher.OrcidProfile)
                .Collection(profile => profile.Works!)
                .LoadAsync(cancellationToken);
            LogStageLoaded(researcher.PersonelId, "orcid-works", Stopwatch.GetElapsedTime(startedAt),
                researcher.OrcidProfile.Works?.Count ?? 0);
        }

        if (researcher.GoogleScholarProfile is not null)
        {
            LogStageStarted(researcher.PersonelId, "google-scholar-works");
            long startedAt = Stopwatch.GetTimestamp();
            await _dbContext.Entry(researcher.GoogleScholarProfile)
                .Collection(profile => profile.Works!)
                .LoadAsync(cancellationToken);
            LogStageLoaded(researcher.PersonelId, "google-scholar-works", Stopwatch.GetElapsedTime(startedAt),
                researcher.GoogleScholarProfile.Works?.Count ?? 0);
        }

        if (researcher.OpenAlexProfile is not null)
        {
            LogStageStarted(researcher.PersonelId, "openalex-works");
            long startedAt = Stopwatch.GetTimestamp();
            await _dbContext.Entry(researcher.OpenAlexProfile)
                .Collection(profile => profile.Works!)
                .LoadAsync(cancellationToken);
            LogStageLoaded(researcher.PersonelId, "openalex-works", Stopwatch.GetElapsedTime(startedAt),
                researcher.OpenAlexProfile.Works?.Count ?? 0);
        }

        if (researcher.ScopusProfile is not null)
        {
            LogStageStarted(researcher.PersonelId, "scopus-works");
            long startedAt = Stopwatch.GetTimestamp();
            await _dbContext.Entry(researcher.ScopusProfile)
                .Collection(profile => profile.Works!)
                .LoadAsync(cancellationToken);
            LogStageLoaded(researcher.PersonelId, "scopus-works", Stopwatch.GetElapsedTime(startedAt),
                researcher.ScopusProfile.Works?.Count ?? 0);
        }

        if (researcher.WebOfScienceProfile is not null)
        {
            LogStageStarted(researcher.PersonelId, "wos-works");
            long startedAt = Stopwatch.GetTimestamp();
            await _dbContext.Entry(researcher.WebOfScienceProfile)
                .Collection(profile => profile.Works!)
                .LoadAsync(cancellationToken);
            LogStageLoaded(researcher.PersonelId, "wos-works", Stopwatch.GetElapsedTime(startedAt),
                researcher.WebOfScienceProfile.Works?.Count ?? 0);
            LogStageStarted(researcher.PersonelId, "wos-peer-reviews");
            startedAt = Stopwatch.GetTimestamp();
            await _dbContext.Entry(researcher.WebOfScienceProfile)
                .Collection(profile => profile.PeerReviews!)
                .LoadAsync(cancellationToken);
            LogStageLoaded(researcher.PersonelId, "wos-peer-reviews", Stopwatch.GetElapsedTime(startedAt),
                researcher.WebOfScienceProfile.PeerReviews?.Count ?? 0);
        }

        if (researcher.TrDizinProfile is not null)
        {
            LogStageStarted(researcher.PersonelId, "trdizin-works");
            long startedAt = Stopwatch.GetTimestamp();
            await _dbContext.Entry(researcher.TrDizinProfile)
                .Collection(profile => profile.Works!)
                .LoadAsync(cancellationToken);
            LogStageLoaded(researcher.PersonelId, "trdizin-works", Stopwatch.GetElapsedTime(startedAt),
                researcher.TrDizinProfile.Works?.Count ?? 0);
            LogStageStarted(researcher.PersonelId, "trdizin-projects");
            startedAt = Stopwatch.GetTimestamp();
            await _dbContext.Entry(researcher.TrDizinProfile)
                .Collection(profile => profile.Projects!)
                .LoadAsync(cancellationToken);
            LogStageLoaded(researcher.PersonelId, "trdizin-projects", Stopwatch.GetElapsedTime(startedAt),
                researcher.TrDizinProfile.Projects?.Count ?? 0);
        }

        LogStageStarted(researcher.PersonelId, "academic-works");
        long academicWorksStartedAt = Stopwatch.GetTimestamp();
        await _dbContext.Entry(researcher)
            .Collection(item => item.AcademicWorks!)
            .LoadAsync(cancellationToken);
        LogStageLoaded(researcher.PersonelId, "academic-works",
            Stopwatch.GetElapsedTime(academicWorksStartedAt), researcher.AcademicWorks?.Count ?? 0);
    }

    private void LogStageStarted(string personelId, string stage)
    {
        _logger.LogInformation(
            "Researcher graph stage started for {PersonelId}: {Stage}.",
            personelId,
            stage);
    }

    private void LogStageLoaded(
        string personelId,
        string stage,
        TimeSpan elapsed,
        int count)
    {
        _logger.LogInformation(
            "Researcher graph stage loaded for {PersonelId}: {Stage}, {Count} rows in {ElapsedMilliseconds} ms.",
            personelId,
            stage,
            count,
            elapsed.TotalMilliseconds);
    }

    private void UpdateResearcher(Researcher target, Researcher source)
    {
        ApplyRequestValues(target, source);
        target.LastUpdatedAt = DateTime.UtcNow;

        UpdateOrcid(target, source);
        UpdateGoogleScholar(target, source);
        UpdateOpenAlex(target, source);
        UpdateScopus(target, source);
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
        profile.ProjectCandidateCount = incoming.ProjectCandidateCount;
        profile.ProjectMatchedCount = incoming.ProjectMatchedCount;
        profile.ProjectUnmatchedCount = incoming.ProjectUnmatchedCount;
        profile.ProjectSearchComplete = incoming.ProjectSearchComplete;
        profile.LastUpdatedAt = incoming.LastUpdatedAt; profile.RawAuthorJson = incoming.RawAuthorJson;
        profile.RawPublicationsJson = incoming.RawPublicationsJson;
        profile.RawProjectsJson = incoming.RawProjectsJson;
        _dbContext.TrDizinWorks.RemoveRange(profile.Works ?? []); profile.Works = incoming.Works;
        _dbContext.TrDizinProjects.RemoveRange(profile.Projects ?? []);
        profile.Projects = incoming.Projects;
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

    private void UpdateScopus(Researcher target, Researcher source)
    {
        ScopusProfile? incoming = source.ScopusProfile;
        if (incoming is null)
            return;
        if (target.ScopusProfile is null)
        {
            target.ScopusProfile = incoming;
            return;
        }

        ScopusProfile profile = target.ScopusProfile;
        profile.ScopusAuthorId = incoming.ScopusAuthorId;
        profile.DisplayName = incoming.DisplayName;
        profile.CurrentAffiliation = incoming.CurrentAffiliation;
        profile.DocumentsCount = incoming.DocumentsCount;
        profile.CitationCount = incoming.CitationCount;
        profile.CitedByCount = incoming.CitedByCount;
        profile.HIndex = incoming.HIndex;
        profile.LastUpdatedAt = incoming.LastUpdatedAt;
        profile.RawDataJson = incoming.RawDataJson;
        profile.SearchPagesJson = incoming.SearchPagesJson;
        _dbContext.ScopusWorks.RemoveRange(profile.Works ?? []);
        profile.Works = incoming.Works;
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
        target.OrcidProfile.ActivitiesDetailsJson = source.OrcidProfile.ActivitiesDetailsJson;
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
