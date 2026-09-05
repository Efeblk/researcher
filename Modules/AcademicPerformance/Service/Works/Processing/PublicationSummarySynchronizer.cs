using System.Security.Cryptography;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public sealed class PublicationSummarySynchronizer
{
    private readonly AcademicDbContext _dbContext;

    public PublicationSummarySynchronizer(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SyncAsync(int researcherId)
    {
        List<AcademicWork> works = await _dbContext.AcademicWorks
            .Where(work => work.ResearcherId == researcherId)
            .OrderBy(work => work.Id)
            .ToListAsync();
        List<PublicationSummary> existing = await _dbContext.PublicationSummaries
            .Include(summary => summary.DisplayApproval)
            .Where(summary => summary.ResearcherId == researcherId)
            .OrderBy(summary => summary.Id)
            .ToListAsync();
        Dictionary<PublicationSummary, List<AcademicWork>> desiredGroups = CreateGroups(works)
            .ToDictionary(group => CreateSummary(researcherId, group));
        List<PublicationSummary> desired = desiredGroups.Keys.ToList();
        Dictionary<string, PublicationSummary> desiredByFingerprint = desired
            .ToDictionary(summary => summary.Fingerprint, StringComparer.Ordinal);
        Dictionary<PublicationSummary, List<PublicationSummary>> existingMatches = desired
            .ToDictionary(summary => summary, _ => new List<PublicationSummary>());
        HashSet<int> retainedIds = [];

        foreach (PublicationSummary summary in existing)
        {
            if (desiredByFingerprint.TryGetValue(summary.Fingerprint, out PublicationSummary? exact))
            {
                existingMatches[exact].Add(summary);
                continue;
            }

            // Evaluate ambiguity once per stored summary, stopping at two
            // matches. Only an unambiguous match can transfer a selection.
            List<PublicationSummary> candidates = desired
                .Where(candidate => IsSamePublication(summary, candidate, desiredGroups[candidate]))
                .Take(2)
                .ToList();
            if (candidates.Count == 1)
                existingMatches[candidates[0]].Add(summary);
        }

        foreach (PublicationSummary candidate in desired)
        {
            List<PublicationSummary> matches = existingMatches[candidate]
                .OrderByDescending(summary => summary.DisplayApproval is not null)
                .ThenByDescending(summary => summary.Fingerprint == candidate.Fingerprint)
                .ThenBy(summary => summary.Id)
                .ToList();
            PublicationSummary? target = matches.FirstOrDefault();

            if (target is null)
            {
                _dbContext.PublicationSummaries.Add(candidate);
                continue;
            }

            CopyValues(candidate, target);
            retainedIds.Add(target.Id);
        }

        _dbContext.PublicationSummaries.RemoveRange(
            existing.Where(summary => !retainedIds.Contains(summary.Id)));
        await _dbContext.SaveChangesAsync();
        return desired.Count;
    }

    private static List<List<AcademicWork>> CreateGroups(List<AcademicWork> works)
    {
        // Establish DOI groups first, so a DOI-less record cannot bridge two
        // conflicting DOIs or split one DOI into multiple summary fingerprints.
        List<List<AcademicWork>> groups = works
            .Where(work => NormalizeDoi(work.Doi) is not null)
            .GroupBy(work => NormalizeDoi(work.Doi), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.ToList())
            .ToList();

        foreach (AcademicWork work in works
            .Where(work => NormalizeDoi(work.Doi) is null)
            .OrderByDescending(work => work.PublicationYear.HasValue)
            .ThenBy(work => NormalizeTitle(work.Title), StringComparer.Ordinal)
            .ThenBy(work => work.PublicationYear)
            .ThenBy(work => work.Id))
        {
            string title = NormalizeTitle(work.Title);
            if (title.Length == 0)
            {
                groups.Add([work]);
                continue;
            }

            List<List<AcademicWork>> matches = groups.Where(group => group.Any(existing =>
                NormalizeTitle(existing.Title) == title &&
                YearsAreCompatible(existing.PublicationYear, work.PublicationYear))).ToList();
            List<AcademicWork>? exactTitleGroup = matches.FirstOrDefault(group =>
                group.All(existing => NormalizeDoi(existing.Doi) is null) &&
                group.All(existing => existing.PublicationYear == work.PublicationYear));
            List<AcademicWork>? target = exactTitleGroup ??
                (matches.Count == 1 ? matches[0] : null);

            if (target is null)
                groups.Add([work]);
            else
                target.Add(work);
        }

        return groups;
    }

    private static bool IsSamePublication(
        PublicationSummary existing,
        PublicationSummary candidate,
        List<AcademicWork> candidateWorks)
    {
        string? existingDoi = NormalizeDoi(existing.Doi);
        string? candidateDoi = NormalizeDoi(candidate.Doi);

        if (!string.IsNullOrWhiteSpace(existingDoi) &&
            !string.IsNullOrWhiteSpace(candidateDoi))
        {
            return existingDoi == candidateDoi;
        }

        // Placeholder titles are not publication identities.
        if (existing.Title == "Başlıksız yayın" || candidate.Title == "Başlıksız yayın")
            return false;

        string? existingTitle = NormalizeTitle(existing.Title);
        return !string.IsNullOrWhiteSpace(existingTitle) &&
            candidateWorks.Any(work => existingTitle == NormalizeTitle(work.Title) &&
                YearsAreCompatible(existing.PublicationYear, work.PublicationYear));
    }

    private static bool YearsAreCompatible(int? first, int? second)
    {
        return !first.HasValue || !second.HasValue || first == second;
    }

    private static PublicationSummary CreateSummary(
        int researcherId,
        List<AcademicWork> works)
    {
        List<AcademicWork>? preferredWorks = works
            .OrderByDescending(work => work.Provider == AcademicWorkProvider.Orcid)
            .ThenByDescending(GetMetadataScore)
            .ThenBy(work => work.Id)
            .ToList();
        string? doi = FirstText(preferredWorks, work => NormalizeDoi(work.Doi));
        string? title = FirstText(preferredWorks, work => work.Title) ?? "Başlıksız yayın";
        int? publicationYear = preferredWorks
            .Select(work => work.PublicationYear)
            .FirstOrDefault(value => value.HasValue);

        PublicationSummary? summary = new PublicationSummary();
        summary.ResearcherId = researcherId;
        summary.Fingerprint = CreateFingerprint(doi, title, publicationYear,
            preferredWorks.All(work => NormalizeTitle(work.Title).Length == 0)
                ? $"work:{preferredWorks[0].Id}"
                : null);
        summary.Title = title;
        summary.PublicationYear = publicationYear;
        summary.Doi = doi;
        summary.Category = preferredWorks
            .Select(work => work.Category)
            .FirstOrDefault(category => category != AcademicWorkCategory.Unknown);
        summary.Authors = FirstText(preferredWorks, work => work.Authors);
        summary.Publication = FirstText(preferredWorks, work => work.Publication);
        summary.PublicationUrl = FirstText(preferredWorks,
            work => work.Link ?? work.SourceUrl ?? work.OpenAccessUrl);
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
        foreach (AcademicWork work in works)
        {
            string? value = selector(work);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? NormalizeDoi(string? doi)
    {
        string? normalized = doi?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (normalized.StartsWith("doi:", StringComparison.Ordinal))
            normalized = normalized[4..].Trim();

        foreach (string prefix in new[] { "https://doi.org/", "http://doi.org/", "https://dx.doi.org/", "http://dx.doi.org/" })
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                normalized = normalized[prefix.Length..].Trim();
                break;
            }
        }
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        StringBuilder normalized = new();
        foreach (char character in title)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return normalized.ToString();
    }

    private static string CreateFingerprint(
        string? doi,
        string title,
        int? publicationYear,
        string? fallbackIdentity = null)
    {
        string source = !string.IsNullOrWhiteSpace(doi)
            ? "doi:" + doi
            : fallbackIdentity ?? $"title:{NormalizeTitle(title)}|year:{publicationYear}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CopyValues(
        PublicationSummary source,
        PublicationSummary target)
    {
        target.ResearcherId = source.ResearcherId;
        target.Fingerprint = source.Fingerprint;
        target.Title = source.Title;
        target.PublicationYear = source.PublicationYear;
        target.Doi = source.Doi;
        target.Category = source.Category;
        target.Authors = source.Authors;
        target.Publication = source.Publication;
        target.PublicationUrl = source.PublicationUrl;
        target.Sources = source.Sources;
        target.UpdatedAt = source.UpdatedAt;
    }
}
