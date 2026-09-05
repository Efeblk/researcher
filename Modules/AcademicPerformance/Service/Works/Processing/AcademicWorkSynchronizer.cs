using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public sealed class AcademicWorkSynchronizer
{
    private readonly AcademicDbContext _dbContext;

    public AcademicWorkSynchronizer(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SyncAsync(Researcher researcher)
    {
        AcademicWork? synchronizedWork = null;
        AcademicWork? existingWork = null;
        DateTime synchronizedAt = DateTime.UtcNow;
        int index = 0;

        List<AcademicWork>? existingWorks = await _dbContext.AcademicWorks
            .Where(work => work.ResearcherId == researcher.Id)
            .ToListAsync();
        List<AcademicWork>? synchronizedWorks = [];
        HashSet<int>? matchedExistingIds = [];

        AddOrcidWorks(
            synchronizedWorks,
            researcher.Id,
            researcher.OrcidProfile?.Works,
            synchronizedAt);
        AddGoogleScholarWorks(
            synchronizedWorks,
            researcher.Id,
            researcher.GoogleScholarProfile?.Works,
            synchronizedAt);
        AddWebOfScienceWorks(
            synchronizedWorks,
            researcher.Id,
            researcher.WebOfScienceProfile?.Works,
            synchronizedAt);

        for (index = 0; index < synchronizedWorks.Count; index++)
        {
            synchronizedWork = synchronizedWorks[index];
            existingWork = FindMatchingWork(
                existingWorks,
                matchedExistingIds,
                synchronizedWork);

            if (existingWork is null)
            {
                _dbContext.AcademicWorks.Add(synchronizedWork);
                continue;
            }

            CopyValues(synchronizedWork, existingWork);
            matchedExistingIds.Add(existingWork.Id);
        }

        for (index = 0; index < existingWorks.Count; index++)
        {
            existingWork = existingWorks[index];

            if (existingWork.Provider != AcademicWorkProvider.Yoksis &&
                !matchedExistingIds.Contains(existingWork.Id))
            {
                _dbContext.AcademicWorks.Remove(existingWork);
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private static void AddGoogleScholarWorks(
        List<AcademicWork> target,
        int researcherId,
        List<GoogleScholarWork>? source,
        DateTime synchronizedAt)
    {
        if (source is null)
        {
            return;
        }

        foreach (GoogleScholarWork sourceWork in source)
        {
            target.Add(new AcademicWork
            {
                ResearcherId = researcherId,
                Provider = AcademicWorkProvider.GoogleScholar,
                ProviderWorkId = sourceWork.CitationId,
                Title = sourceWork.Title,
                PublicationYear = sourceWork.PublicationYear,
                Category = AcademicWorkCategory.Unknown,
                CategorySource = AcademicWorkCategorySource.Unknown,
                CitedByCount = sourceWork.CitedByCount,
                Authors = sourceWork.Authors,
                Publication = sourceWork.Publication,
                Link = sourceWork.Url,
                SourceId = sourceWork.CitationId,
                SourceName = "Google Scholar",
                SourceType = "Google Scholar",
                SourceUrl = sourceWork.Url,
                ProviderPayload = sourceWork.RawDataJson,
                SyncedAt = synchronizedAt
            });
        }
    }

    private static AcademicWork? FindMatchingWork(
        List<AcademicWork> existingWorks,
        HashSet<int> matchedExistingIds,
        AcademicWork synchronizedWork)
    {
        AcademicWork? existingWork = null;
        int index = 0;

        for (index = 0; index < existingWorks.Count; index++)
        {
            existingWork = existingWorks[index];

            if (matchedExistingIds.Contains(existingWork.Id) ||
                existingWork.Provider != synchronizedWork.Provider)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(synchronizedWork.ProviderWorkId) &&
                string.Equals(
                    existingWork.ProviderWorkId,
                    synchronizedWork.ProviderWorkId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return existingWork;
            }

            if (string.IsNullOrWhiteSpace(synchronizedWork.ProviderWorkId) &&
                string.IsNullOrWhiteSpace(existingWork.ProviderWorkId) &&
                string.Equals(
                    existingWork.Title,
                    synchronizedWork.Title,
                    StringComparison.OrdinalIgnoreCase) &&
                existingWork.PublicationYear == synchronizedWork.PublicationYear)
            {
                return existingWork;
            }
        }

        return null;
    }

    private static void CopyValues(AcademicWork source, AcademicWork target)
    {
        target.ResearcherId = source.ResearcherId;
        target.Provider = source.Provider;
        target.ProviderWorkId = source.ProviderWorkId;
        target.Title = source.Title;
        target.PublicationYear = source.PublicationYear;
        target.PublicationDate = source.PublicationDate;
        target.Doi = source.Doi;
        target.RawType = source.RawType;
        target.Category = source.Category;
        target.CategorySource = source.CategorySource;
        target.CitedByCount = source.CitedByCount;
        target.ReferencedWorksCount = source.ReferencedWorksCount;
        target.Authors = source.Authors;
        target.Institutions = source.Institutions;
        target.Abstract = source.Abstract;
        target.Keywords = source.Keywords;
        target.Topics = source.Topics;
        target.Language = source.Language;
        target.Publication = source.Publication;
        target.Volume = source.Volume;
        target.Issue = source.Issue;
        target.FirstPage = source.FirstPage;
        target.LastPage = source.LastPage;
        target.Link = source.Link;
        target.SourceId = source.SourceId;
        target.SourceName = source.SourceName;
        target.SourceType = source.SourceType;
        target.SourceUrl = source.SourceUrl;
        target.IsOpenAccess = source.IsOpenAccess;
        target.OpenAccessStatus = source.OpenAccessStatus;
        target.OpenAccessUrl = source.OpenAccessUrl;
        target.HasFullText = source.HasFullText;
        target.FullTextUrl = source.FullTextUrl;
        target.License = source.License;
        target.Version = source.Version;
        target.IsRetracted = source.IsRetracted;
        target.ProviderPayload = source.ProviderPayload;
        target.SyncedAt = source.SyncedAt;
    }

    private static void AddOrcidWorks(
        List<AcademicWork> target,
        int researcherId,
        List<OrcidWork>? source,
        DateTime synchronizedAt)
    {
        OrcidWork? sourceWork = null;
        AcademicWork? academicWork = null;
        int index = 0;

        if (source is null)
        {
            return;
        }

        for (index = 0; index < source.Count; index++)
        {
            sourceWork = source[index];
            academicWork = new AcademicWork();
            academicWork.ResearcherId = researcherId;
            academicWork.Provider = AcademicWorkProvider.Orcid;
            academicWork.ProviderWorkId = sourceWork.PutCode.ToString();
            academicWork.Title = sourceWork.Title;
            academicWork.PublicationYear = sourceWork.PublicationYear;
            academicWork.PublicationDate = sourceWork.PublicationDate;
            academicWork.Doi = sourceWork.Doi;
            academicWork.RawType = sourceWork.WorkType;
            academicWork.Category = sourceWork.Category;
            academicWork.CategorySource = sourceWork.CategorySource;
            academicWork.CitedByCount = null;
            academicWork.ReferencedWorksCount = null;
            academicWork.Authors = sourceWork.Authors;
            academicWork.Abstract = sourceWork.ShortDescription;
            academicWork.Language = sourceWork.LanguageCode;
            academicWork.Publication = sourceWork.JournalTitle;
            academicWork.Link = sourceWork.Url;
            academicWork.SourceId = sourceWork.PutCode.ToString();
            academicWork.SourceName = sourceWork.SourceName;
            academicWork.SourceType = "ORCID";
            academicWork.SourceUrl = sourceWork.Url;
            academicWork.ProviderPayload = sourceWork.RawDataJson;
            academicWork.SyncedAt = synchronizedAt;
            target.Add(academicWork);
        }
    }

    private static void AddWebOfScienceWorks(
        List<AcademicWork> target,
        int researcherId,
        List<WebOfScienceWork>? source,
        DateTime synchronizedAt)
    {
        WebOfScienceWork? sourceWork = null;
        AcademicWork? academicWork = null;
        int index = 0;

        if (source is null)
        {
            return;
        }

        for (index = 0; index < source.Count; index++)
        {
            sourceWork = source[index];
            academicWork = new AcademicWork();
            academicWork.ResearcherId = researcherId;
            academicWork.Provider = AcademicWorkProvider.WebOfScience;
            academicWork.ProviderWorkId = sourceWork.Uid;
            academicWork.Title = sourceWork.Title;
            academicWork.PublicationYear = sourceWork.PublicationYear;
            academicWork.PublicationDate = sourceWork.PublicationDate;
            academicWork.Doi = sourceWork.Doi;
            academicWork.RawType = sourceWork.WorkTypes;
            academicWork.Category = sourceWork.Category;
            academicWork.CategorySource = sourceWork.CategorySource;
            academicWork.CitedByCount = sourceWork.TimesCited;
            academicWork.Publication = sourceWork.SourceTitle;
            academicWork.Volume = sourceWork.Volume;
            academicWork.Issue = sourceWork.Issue;
            academicWork.SourceId = sourceWork.Uid;
            academicWork.SourceName = sourceWork.SourceTitle;
            academicWork.SourceType = "Web of Science";
            academicWork.ProviderPayload = sourceWork.RawDataJson;
            academicWork.SyncedAt = synchronizedAt;
            target.Add(academicWork);
        }
    }
}
