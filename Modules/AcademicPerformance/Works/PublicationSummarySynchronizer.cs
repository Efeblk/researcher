using System.Security.Cryptography;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works;

public sealed class PublicationSummarySynchronizer
{
    private readonly AcademicDbContext _dbContext;

    public PublicationSummarySynchronizer(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SyncAsync(int researcherId)
    {
        List<AcademicWork>? works = null;
        List<PublicationSummary>? existingSummaries = null;
        List<List<AcademicWork>>? groups = null;
        HashSet<int>? retainedIds = null;
        PublicationSummary? synchronizedSummary = null;
        PublicationSummary? existingSummary = null;
        int index = 0;

        works = await _dbContext.AcademicWorks
            .Where(work => work.ResearcherId == researcherId)
            .ToListAsync();
        existingSummaries = await _dbContext.PublicationSummaries
            .Where(summary => summary.ResearcherId == researcherId)
            .ToListAsync();
        groups = CreateGroups(works);
        retainedIds = [];

        for (index = 0; index < groups.Count; index++)
        {
            synchronizedSummary = CreateSummary(researcherId, groups[index]);
            existingSummary = existingSummaries.FirstOrDefault(summary =>
                summary.Fingerprint == synchronizedSummary.Fingerprint);

            if (existingSummary is null)
            {
                _dbContext.PublicationSummaries.Add(synchronizedSummary);
                continue;
            }

            CopyValues(synchronizedSummary, existingSummary);
            retainedIds.Add(existingSummary.Id);
        }

        for (index = 0; index < existingSummaries.Count; index++)
        {
            existingSummary = existingSummaries[index];

            if (!retainedIds.Contains(existingSummary.Id))
            {
                _dbContext.PublicationSummaries.Remove(existingSummary);
            }
        }

        await _dbContext.SaveChangesAsync();
        return groups.Count;
    }

    private static List<List<AcademicWork>> CreateGroups(List<AcademicWork> works)
    {
        List<List<AcademicWork>>? groups = null;
        List<AcademicWork>? matchingGroup = null;
        AcademicWork? work = null;
        int index = 0;

        groups = [];

        for (index = 0; index < works.Count; index++)
        {
            work = works[index];
            matchingGroup = groups.FirstOrDefault(group => IsSamePublication(group, work));

            if (matchingGroup is null)
            {
                groups.Add([work]);
                continue;
            }

            matchingGroup.Add(work);
        }

        return groups;
    }

    private static bool IsSamePublication(
        List<AcademicWork> group,
        AcademicWork candidate)
    {
        string? candidateDoi = null;
        string? candidateTitle = null;
        string? existingDoi = null;
        int index = 0;
        AcademicWork? existing = null;

        candidateDoi = NormalizeDoi(candidate.Doi);
        candidateTitle = NormalizeTitle(candidate.Title);

        for (index = 0; index < group.Count; index++)
        {
            existing = group[index];
            existingDoi = NormalizeDoi(existing.Doi);

            if (!string.IsNullOrWhiteSpace(candidateDoi) &&
                !string.IsNullOrWhiteSpace(existingDoi))
            {
                if (candidateDoi == existingDoi)
                {
                    return true;
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidateTitle) &&
                candidateTitle == NormalizeTitle(existing.Title) &&
                YearsAreCompatible(candidate.PublicationYear, existing.PublicationYear))
            {
                return true;
            }
        }

        return false;
    }

    private static bool YearsAreCompatible(int? first, int? second)
    {
        return !first.HasValue || !second.HasValue || first == second;
    }

    private static PublicationSummary CreateSummary(
        int researcherId,
        List<AcademicWork> works)
    {
        List<AcademicWork>? preferredWorks = null;
        PublicationSummary? summary = null;
        string? doi = null;
        string? title = null;
        int? publicationYear = null;

        preferredWorks = works
            .OrderByDescending(work => work.Provider == AcademicWorkProvider.Orcid)
            .ThenByDescending(GetMetadataScore)
            .ToList();
        doi = FirstText(preferredWorks, work => NormalizeDoi(work.Doi));
        title = FirstText(preferredWorks, work => work.Title) ?? "Başlıksız yayın";
        publicationYear = preferredWorks
            .Select(work => work.PublicationYear)
            .FirstOrDefault(value => value.HasValue);

        summary = new PublicationSummary();
        summary.ResearcherId = researcherId;
        summary.Fingerprint = CreateFingerprint(doi, title, publicationYear);
        summary.Title = title;
        summary.PublicationYear = publicationYear;
        summary.PublicationDate = preferredWorks
            .Select(work => work.PublicationDate)
            .FirstOrDefault(value => value.HasValue);
        summary.Doi = doi;
        summary.Category = preferredWorks
            .Select(work => work.Category)
            .FirstOrDefault(category => category != AcademicWorkCategory.Unknown);
        summary.Authors = FirstText(preferredWorks, work => work.Authors);
        summary.Abstract = FirstText(preferredWorks, work => work.Abstract);
        summary.Keywords = FirstText(preferredWorks, work => work.Keywords);
        summary.Topics = FirstText(preferredWorks, work => work.Topics);
        summary.Language = FirstText(preferredWorks, work => work.Language);
        summary.Publication = FirstText(preferredWorks, work => work.Publication);
        summary.Volume = FirstText(preferredWorks, work => work.Volume);
        summary.Issue = FirstText(preferredWorks, work => work.Issue);
        summary.FirstPage = FirstText(preferredWorks, work => work.FirstPage);
        summary.LastPage = FirstText(preferredWorks, work => work.LastPage);
        summary.CitedByCount = preferredWorks
            .Where(work => work.CitedByCount.HasValue)
            .Select(work => work.CitedByCount)
            .Max();
        summary.IsOpenAccess = ResolveBoolean(preferredWorks, work => work.IsOpenAccess);
        summary.IsRetracted = ResolveBoolean(preferredWorks, work => work.IsRetracted);
        summary.PublicationUrl = FirstText(preferredWorks,
            work => work.Link ?? work.SourceUrl ?? work.OpenAccessUrl);
        summary.PdfUrl = FirstText(preferredWorks,
            work => work.FullTextUrl);
        summary.Sources = string.Join(",",
            preferredWorks
                .Select(work => work.Provider.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value));
        summary.UpdatedAt = preferredWorks.Max(work => work.SyncedAt);

        return summary;
    }

    private static int GetMetadataScore(AcademicWork work)
    {
        int score = 0;

        score += string.IsNullOrWhiteSpace(work.Doi) ? 0 : 1;
        score += string.IsNullOrWhiteSpace(work.Authors) ? 0 : 1;
        score += string.IsNullOrWhiteSpace(work.Abstract) ? 0 : 1;
        score += string.IsNullOrWhiteSpace(work.Publication) ? 0 : 1;
        score += string.IsNullOrWhiteSpace(work.FullTextUrl) ? 0 : 1;

        return score;
    }

    private static string? FirstText(
        List<AcademicWork> works,
        Func<AcademicWork, string?> selector)
    {
        string? value = null;
        int index = 0;

        for (index = 0; index < works.Count; index++)
        {
            value = selector(works[index]);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool? ResolveBoolean(
        List<AcademicWork> works,
        Func<AcademicWork, bool?> selector)
    {
        List<bool>? values = null;

        values = works
            .Select(selector)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        return values.Any(value => value);
    }

    private static string? NormalizeDoi(string? doi)
    {
        string? normalized = null;

        normalized = doi?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = normalized
            .Replace("https://doi.org/", string.Empty, StringComparison.Ordinal)
            .Replace("http://doi.org/", string.Empty, StringComparison.Ordinal);

        return normalized.StartsWith("doi:", StringComparison.Ordinal)
            ? normalized[4..].Trim()
            : normalized;
    }

    private static string NormalizeTitle(string? title)
    {
        StringBuilder? normalized = null;
        int index = 0;

        normalized = new StringBuilder();

        if (string.IsNullOrWhiteSpace(title))
        {
            return normalized.ToString();
        }

        for (index = 0; index < title.Length; index++)
        {
            if (char.IsLetterOrDigit(title[index]))
            {
                normalized.Append(char.ToLowerInvariant(title[index]));
            }
        }

        return normalized.ToString();
    }

    private static string CreateFingerprint(
        string? doi,
        string title,
        int? publicationYear)
    {
        string? source = null;
        byte[]? hash = null;

        source = !string.IsNullOrWhiteSpace(doi)
            ? "doi:" + doi
            : $"title:{NormalizeTitle(title)}|year:{publicationYear}";
        hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CopyValues(
        PublicationSummary source,
        PublicationSummary target)
    {
        target.ResearcherId = source.ResearcherId;
        target.Title = source.Title;
        target.PublicationYear = source.PublicationYear;
        target.PublicationDate = source.PublicationDate;
        target.Doi = source.Doi;
        target.Category = source.Category;
        target.Authors = source.Authors;
        target.Abstract = source.Abstract;
        target.Keywords = source.Keywords;
        target.Topics = source.Topics;
        target.Language = source.Language;
        target.Publication = source.Publication;
        target.Volume = source.Volume;
        target.Issue = source.Issue;
        target.FirstPage = source.FirstPage;
        target.LastPage = source.LastPage;
        target.CitedByCount = source.CitedByCount;
        target.IsOpenAccess = source.IsOpenAccess;
        target.IsRetracted = source.IsRetracted;
        target.PublicationUrl = source.PublicationUrl;
        target.PdfUrl = source.PdfUrl;
        target.Sources = source.Sources;
        target.UpdatedAt = source.UpdatedAt;
    }
}
