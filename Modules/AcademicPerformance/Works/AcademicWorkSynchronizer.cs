using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works;

public sealed class AcademicWorkSynchronizer
{
    private readonly AcademicDbContext _dbContext;

    public AcademicWorkSynchronizer(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SyncAsync(Researcher researcher)
    {
        List<AcademicWork>? existingWorks = null;
        List<AcademicWork>? synchronizedWorks = null;
        HashSet<int>? matchedExistingIds = null;
        AcademicWork? synchronizedWork = null;
        AcademicWork? existingWork = null;
        DateTime synchronizedAt = default;
        int index = 0;

        existingWorks = await _dbContext.AcademicWorks
            .Where(work => work.ResearcherId == researcher.Id)
            .ToListAsync();
        synchronizedWorks = [];
        matchedExistingIds = [];
        synchronizedAt = DateTime.UtcNow;

        AddOpenAlexWorks(
            synchronizedWorks,
            researcher.Id,
            researcher.OpenAlex?.Works,
            synchronizedAt);
        AddGoogleScholarWorks(
            synchronizedWorks,
            researcher.Id,
            researcher.GoogleScholar?.Works,
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

            if (!matchedExistingIds.Contains(existingWork.Id))
            {
                _dbContext.AcademicWorks.Remove(existingWork);
            }
        }

        await _dbContext.SaveChangesAsync();
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
        target.CitedByUrl = source.CitedByUrl;
        target.CitedBySerpApiUrl = source.CitedBySerpApiUrl;
        target.CitesId = source.CitesId;
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
        target.ProviderDetailPayload = source.ProviderDetailPayload;
        target.SyncedAt = source.SyncedAt;
    }

    private static void AddOpenAlexWorks(
        List<AcademicWork> target,
        int researcherId,
        List<OpenAlexWork>? source,
        DateTime synchronizedAt)
    {
        int index = 0;
        OpenAlexWork? sourceWork = null;
        AcademicWork? academicWork = null;

        if (source is null)
        {
            return;
        }

        for (index = 0; index < source.Count; index++)
        {
            sourceWork = source[index];
            academicWork = new AcademicWork();
            academicWork.ResearcherId = researcherId;
            academicWork.Provider = AcademicWorkProvider.OpenAlex;
            academicWork.ProviderWorkId = sourceWork.WorkId;
            academicWork.Title = sourceWork.Title;
            academicWork.PublicationYear = sourceWork.PublicationYear;
            academicWork.PublicationDate = sourceWork.PublicationDate;
            academicWork.Doi = sourceWork.Doi;
            academicWork.RawType = sourceWork.Type;
            academicWork.Category = sourceWork.Category;
            academicWork.CategorySource = sourceWork.CategorySource;
            academicWork.CitedByCount = sourceWork.CitedByCount;
            academicWork.ReferencedWorksCount = sourceWork.ReferencedWorksCount;
            academicWork.Authors = sourceWork.Authors;
            academicWork.Institutions = sourceWork.Institutions;
            academicWork.Abstract = sourceWork.Abstract;
            academicWork.Keywords = sourceWork.Keywords;
            academicWork.Topics = sourceWork.Topics;
            academicWork.Language = sourceWork.Language;
            academicWork.Publication = sourceWork.SourceName;
            academicWork.Volume = sourceWork.Volume;
            academicWork.Issue = sourceWork.Issue;
            academicWork.FirstPage = sourceWork.FirstPage;
            academicWork.LastPage = sourceWork.LastPage;
            academicWork.Link = sourceWork.PrimaryLocation?.LandingPageUrl
                ?? sourceWork.SourceUrl;
            academicWork.SourceId = sourceWork.SourceId;
            academicWork.SourceName = sourceWork.SourceName;
            academicWork.SourceType = sourceWork.SourceType;
            academicWork.SourceUrl = sourceWork.SourceUrl;
            academicWork.IsOpenAccess = sourceWork.IsOpenAccess;
            academicWork.OpenAccessStatus = sourceWork.OpenAccessStatus;
            academicWork.OpenAccessUrl = sourceWork.OpenAccessUrl;
            academicWork.HasFullText = sourceWork.HasFullText;
            academicWork.FullTextUrl = sourceWork.FullTextUrl;
            academicWork.License = sourceWork.License;
            academicWork.Version = sourceWork.Version;
            academicWork.IsRetracted = sourceWork.IsRetracted;
            academicWork.ProviderPayload = sourceWork.RawDataJson;
            academicWork.SyncedAt = synchronizedAt;
            target.Add(academicWork);
        }
    }

    private static void AddGoogleScholarWorks(
        List<AcademicWork> target,
        int researcherId,
        List<GoogleScholarWork>? source,
        DateTime synchronizedAt)
    {
        int index = 0;
        int publicationYear = 0;
        GoogleScholarWork? sourceWork = null;
        AcademicWork? academicWork = null;

        if (source is null)
        {
            return;
        }

        for (index = 0; index < source.Count; index++)
        {
            sourceWork = source[index];
            publicationYear = 0;
            int.TryParse(sourceWork.Year, out publicationYear);

            academicWork = new AcademicWork();
            academicWork.ResearcherId = researcherId;
            academicWork.Provider = AcademicWorkProvider.GoogleScholar;
            academicWork.ProviderWorkId = sourceWork.CitationId;
            academicWork.Title = sourceWork.Title;
            academicWork.PublicationYear = publicationYear > 0
                ? publicationYear
                : null;
            academicWork.Category = sourceWork.Category;
            academicWork.CategorySource = sourceWork.CategorySource;
            academicWork.CitedByCount = sourceWork.CitedByCount;
            academicWork.Authors = sourceWork.Authors;
            academicWork.Publication = sourceWork.Publication;
            academicWork.Link = sourceWork.Link;
            academicWork.CitedByUrl = sourceWork.CitedByUrl;
            academicWork.CitedBySerpApiUrl = sourceWork.CitedBySerpApiUrl;
            academicWork.CitesId = sourceWork.CitesId;
            academicWork.SourceName = sourceWork.Publication;
            academicWork.SourceUrl = sourceWork.Link;
            academicWork.ProviderPayload = sourceWork.RawDataJson;
            academicWork.ProviderDetailPayload = sourceWork.DetailRawDataJson;
            academicWork.SyncedAt = synchronizedAt;
            target.Add(academicWork);
        }
    }

}
