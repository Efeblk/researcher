using System.Data;
using System.Security.Cryptography;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public sealed class PublicationSummarySynchronizer
{
    private readonly AcademicDbContext _dbContext;
    private readonly CanonicalWorkSynchronizer _canonicalWorkSynchronizer;

    public PublicationSummarySynchronizer(
        AcademicDbContext dbContext,
        CanonicalWorkSynchronizer? canonicalWorkSynchronizer = null)
    {
        _dbContext = dbContext;
        _canonicalWorkSynchronizer = canonicalWorkSynchronizer ?? new CanonicalWorkSynchronizer(dbContext);
    }

    public async Task<int> SyncAsync(string personelId, CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? ownedTransaction = null;
        if (_dbContext.Database.CurrentTransaction is null)
            ownedTransaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            await _canonicalWorkSynchronizer.AcquireWriteGateAsync(cancellationToken);
            await _canonicalWorkSynchronizer.AcquireResearcherLockAsync(personelId, cancellationToken);
            List<CanonicalResearcherWork> associations = await _dbContext.CanonicalResearcherWorks
                .AsNoTracking().Where(value => value.PersonelId == personelId)
                .OrderBy(value => value.CanonicalWorkId).ToListAsync(cancellationToken);
            int[] canonicalWorkIds = associations.Select(value => value.CanonicalWorkId).ToArray();
            List<CanonicalWorkObservation> observations = canonicalWorkIds.Length == 0 ? [] :
                await _dbContext.CanonicalWorkObservations.AsNoTracking()
                    .Include(value => value.AcademicWork)
                    .Where(value => value.PersonelId == personelId &&
                        canonicalWorkIds.Contains(value.CanonicalWorkId))
                    .OrderBy(value => value.AcademicWorkId).ToListAsync(cancellationToken);
            Dictionary<int, List<AcademicWork>> worksByCanonicalId = observations
                .Where(value => value.AcademicWork is not null)
                .GroupBy(value => value.CanonicalWorkId)
                .ToDictionary(group => group.Key,
                    group => group.Select(value => value.AcademicWork!).ToList());
            List<DesiredSummary> desired = associations
                .Where(value => worksByCanonicalId.ContainsKey(value.CanonicalWorkId))
                .Select(value => new DesiredSummary(
                    CreateSummary(personelId, value.CanonicalWorkId,
                        worksByCanonicalId[value.CanonicalWorkId]),
                    worksByCanonicalId[value.CanonicalWorkId])).ToList();
            List<PublicationSummary> existing = await _dbContext.PublicationSummaries
                .Include(value => value.DisplayApproval)
                .Where(value => value.PersonelId == personelId)
                .OrderBy(value => value.Id).ToListAsync(cancellationToken);

            Dictionary<int, PublicationSummary> existingByCanonicalId = existing
                .Where(value => value.CanonicalWorkId.HasValue)
                .GroupBy(value => value.CanonicalWorkId!.Value)
                .ToDictionary(group => group.Key, group => group
                    .OrderByDescending(value => value.DisplayApproval is not null)
                    .ThenBy(value => value.Id).First());
            HashSet<int> desiredCanonicalIds = desired
                .Select(value => value.Summary.CanonicalWorkId!.Value).ToHashSet();
            List<PublicationSummary> legacy = existing
                .Where(value => !value.CanonicalWorkId.HasValue ||
                    !desiredCanonicalIds.Contains(value.CanonicalWorkId.Value)).ToList();
            Dictionary<PublicationSummary, List<DesiredSummary>> legacyMatches = legacy
                .ToDictionary(value => value, value => desired
                    .Where(candidate => IsLegacyMatch(value, candidate)).Take(2).ToList());
            Dictionary<DesiredSummary, PublicationSummary?> targets = [];
            HashSet<int> retainedIds = [];

            foreach (DesiredSummary desiredSummary in desired)
            {
                PublicationSummary candidate = desiredSummary.Summary;
                PublicationSummary? exact = existingByCanonicalId.GetValueOrDefault(
                    candidate.CanonicalWorkId!.Value);
                List<PublicationSummary> candidates = legacyMatches
                    .Where(pair => pair.Value.Count == 1 && ReferenceEquals(pair.Value[0], desiredSummary))
                    .Select(pair => pair.Key).ToList();
                if (exact is not null)
                    candidates.Add(exact);
                PublicationSummary? target = candidates.Distinct()
                    .OrderByDescending(value => value.DisplayApproval is not null)
                    .ThenByDescending(value => value.CanonicalWorkId == candidate.CanonicalWorkId)
                    .ThenBy(value => value.Id).FirstOrDefault();
                targets.Add(desiredSummary, target);
                if (target is not null)
                    retainedIds.Add(target.Id);
            }

            _dbContext.PublicationSummaries.RemoveRange(
                existing.Where(value => !retainedIds.Contains(value.Id)));
            await _dbContext.SaveChangesAsync(cancellationToken);
            foreach ((DesiredSummary desiredSummary, PublicationSummary? target) in targets)
            {
                if (target is null)
                    _dbContext.PublicationSummaries.Add(desiredSummary.Summary);
                else
                    CopyValues(desiredSummary.Summary, target);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return desired.Count;
        }
        catch
        {
            if (ownedTransaction is not null)
                await ownedTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }

    private static bool IsLegacyMatch(PublicationSummary existing, DesiredSummary desired)
    {
        PublicationSummary candidate = desired.Summary;
        string? existingDoi = AcademicDoiNormalizer.NormalizeValid(existing.Doi);
        string? candidateDoi = AcademicDoiNormalizer.NormalizeValid(candidate.Doi);
        if (existingDoi is not null && candidateDoi is not null)
            return existingDoi == candidateDoi;
        if (existing.Fingerprint == CreateLegacyFingerprint(candidate))
            return true;
        if (existing.Title == "Başlıksız yayın" || candidate.Title == "Başlıksız yayın")
            return false;
        string normalizedTitle = NormalizeTitle(existing.Title);
        return desired.Works.Any(work => normalizedTitle == NormalizeTitle(work.Title) &&
            YearsAreCompatible(existing.PublicationYear, work.PublicationYear));
    }

    private static bool YearsAreCompatible(int? first, int? second) =>
        !first.HasValue || !second.HasValue || first == second;

    private static PublicationSummary CreateSummary(
        string personelId, int canonicalWorkId, List<AcademicWork> works)
    {
        List<AcademicWork> preferredWorks = works
            .OrderByDescending(work => work.Provider == AcademicWorkProvider.Orcid)
            .ThenByDescending(GetMetadataScore).ThenBy(work => work.Id).ToList();
        string? doi = FirstText(preferredWorks, work => AcademicDoiNormalizer.Normalize(work.Doi));
        string title = FirstText(preferredWorks, work => work.Title) ?? "Başlıksız yayın";
        return new()
        {
            PersonelId = personelId,
            CanonicalWorkId = canonicalWorkId,
            Fingerprint = Hash("canonical-work:" + canonicalWorkId),
            Title = title,
            PublicationYear = preferredWorks.Select(work => work.PublicationYear)
                .FirstOrDefault(value => value.HasValue),
            Doi = doi,
            Category = preferredWorks.Select(work => work.Category)
                .FirstOrDefault(value => value != AcademicWorkCategory.Unknown),
            Authors = FirstText(preferredWorks, work => work.Authors),
            Publication = FirstText(preferredWorks, work => work.Publication),
            PublicationUrl = FirstText(preferredWorks, work => work.Link),
            Sources = string.Join(",", preferredWorks.Select(work => work.Provider.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value)),
            UpdatedAt = preferredWorks.Max(work => work.SyncedAt)
        };
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

    private static string? FirstText(List<AcademicWork> works, Func<AcademicWork, string?> selector)
    {
        foreach (AcademicWork work in works)
        {
            string? value = selector(work);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static string CreateLegacyFingerprint(PublicationSummary summary)
    {
        string? doi = AcademicDoiNormalizer.Normalize(summary.Doi);
        string source = !string.IsNullOrWhiteSpace(doi) ? "doi:" + doi :
            $"title:{NormalizeTitle(summary.Title)}|year:{summary.PublicationYear}";
        return Hash(source);
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;
        StringBuilder normalized = new();
        foreach (char character in title)
            if (char.IsLetterOrDigit(character))
                normalized.Append(char.ToLowerInvariant(character));
        return normalized.ToString();
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void CopyValues(PublicationSummary source, PublicationSummary target)
    {
        target.PersonelId = source.PersonelId;
        target.CanonicalWorkId = source.CanonicalWorkId;
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

    private sealed record DesiredSummary(PublicationSummary Summary, List<AcademicWork> Works);
}
