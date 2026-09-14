using System.Security.Cryptography;
using System.Text;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public static class AnalysisFreshnessStatus
{
    public const string Current = "Current";
    public const string Stale = "Stale";
    public const string Unknown = "Unknown";
}

public sealed record AnalysisFreshnessResult(
    long AnalysisRunId,
    string Status,
    IReadOnlyList<string> Reasons,
    string Identity);

public static class AnalysisFreshnessEvaluator
{
    public static async Task<IReadOnlyDictionary<long, AnalysisFreshnessResult>> EvaluateAsync(
        AnalysisDbContext database,
        IReadOnlyCollection<long> analysisRunIds,
        string currentPolicyVersion,
        CancellationToken cancellationToken)
    {
        long[] ids = analysisRunIds.Where(value => value > 0).Distinct().Order().ToArray();
        if (ids.Length == 0)
            return new Dictionary<long, AnalysisFreshnessResult>();
        List<RunIdentity> runs = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Where(value => ids.Contains(value.Id))
            .Select(value => new RunIdentity(value.Id, value.CanonicalWorkId, value.Language,
                value.PolicyVersion, value.SourceIdentityHash))
            .ToListAsync(cancellationToken);
        int[] workIds = runs.Select(value => value.CanonicalWorkId).Distinct().ToArray();
        IReadOnlyDictionary<int, string> currentSourceIdentities =
            await CanonicalSourceIdentity.LoadAsync(database, workIds, cancellationToken);
        string[] languages = runs.Select(value => value.Language).Distinct().ToArray();
        List<LatestIdentity> latest = workIds.Length == 0 ? [] :
            await database.CanonicalArticleAnalysisRuns.AsNoTracking()
                .Where(value => workIds.Contains(value.CanonicalWorkId) && languages.Contains(value.Language))
                .GroupBy(value => new { value.CanonicalWorkId, value.Language })
                .Select(group => new LatestIdentity(group.Key.CanonicalWorkId, group.Key.Language,
                    group.Max(value => value.Id)))
                .ToListAsync(cancellationToken);
        List<JobIdentity> jobs = workIds.Length == 0 ? [] :
            await database.ArticleSummaryAutomationJobs.AsNoTracking()
                .Where(value => workIds.Contains(value.CanonicalWorkId) && languages.Contains(value.Language))
                .Select(value => new JobIdentity(value.CanonicalWorkId, value.Language,
                    value.LastSuccessfulAnalysisRunId, value.ProcessedInputHash,
                    value.ProcessedPolicyVersion, value.DesiredInputHash, value.DesiredPolicyVersion))
                .ToListAsync(cancellationToken);
        Dictionary<string, LatestIdentity> latestByKey = latest.ToDictionary(
            value => Key(value.CanonicalWorkId, value.Language), StringComparer.Ordinal);
        Dictionary<string, JobIdentity> jobByKey = jobs.ToDictionary(
            value => Key(value.CanonicalWorkId, value.Language), StringComparer.Ordinal);
        Dictionary<long, RunIdentity> runById = runs.ToDictionary(value => value.Id);
        Dictionary<long, AnalysisFreshnessResult> result = [];
        foreach (long id in ids)
        {
            if (!runById.TryGetValue(id, out RunIdentity? run))
            {
                result[id] = Create(id, AnalysisFreshnessStatus.Unknown,
                    ["AnalysisRunUnavailable"], $"{id}:missing:{currentPolicyVersion}");
                continue;
            }
            string key = Key(run.CanonicalWorkId, run.Language);
            latestByKey.TryGetValue(key, out LatestIdentity? newest);
            jobByKey.TryGetValue(key, out JobIdentity? job);
            currentSourceIdentities.TryGetValue(run.CanonicalWorkId, out string? currentSourceIdentity);
            result[id] = Evaluate(run, newest, job, currentPolicyVersion, currentSourceIdentity);
        }
        return result;
    }

    public static string CreateFreshnessHash(
        IEnumerable<AnalysisFreshnessResult> results,
        string currentPolicyVersion)
    {
        string identity = string.Join("\n", results.OrderBy(value => value.AnalysisRunId)
            .Select(value => value.Identity).Prepend("policy:" + currentPolicyVersion));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static AnalysisFreshnessResult Evaluate(RunIdentity run, LatestIdentity? newest,
        JobIdentity? job, string currentPolicyVersion, string? currentSourceIdentity)
    {
        string identity = string.Join(':', run.Id, run.CanonicalWorkId, run.Language,
            run.PolicyVersion, currentPolicyVersion, newest?.RunId.ToString() ?? "missing",
            job?.LastSuccessfulAnalysisRunId?.ToString() ?? "missing",
            job?.ProcessedInputHash ?? "missing", job?.ProcessedPolicyVersion ?? "missing",
            job?.DesiredInputHash ?? "missing", job?.DesiredPolicyVersion ?? "missing",
            run.SourceIdentityHash ?? "missing", currentSourceIdentity ?? "missing");
        if (currentSourceIdentity is null)
            return Create(run.Id, AnalysisFreshnessStatus.Stale,
                ["SourceAssociationUnavailable"], identity);
        if (string.IsNullOrWhiteSpace(run.SourceIdentityHash))
            return Create(run.Id, AnalysisFreshnessStatus.Unknown,
                ["LegacySourceIdentityUnavailable"], identity);
        if (!string.Equals(run.SourceIdentityHash, currentSourceIdentity, StringComparison.Ordinal))
            return Create(run.Id, AnalysisFreshnessStatus.Stale,
                ["SourceIdentityChanged"], identity);
        if (newest is not null && newest.RunId > run.Id)
            return Create(run.Id, AnalysisFreshnessStatus.Stale, ["NewerAnalysisAvailable"], identity);
        if (!string.IsNullOrWhiteSpace(run.PolicyVersion) && !string.IsNullOrWhiteSpace(currentPolicyVersion) &&
            !string.Equals(run.PolicyVersion, currentPolicyVersion, StringComparison.Ordinal))
            return Create(run.Id, AnalysisFreshnessStatus.Stale, ["CurrentPolicyChanged"], identity);
        if (job is null)
            return Create(run.Id, AnalysisFreshnessStatus.Unknown, ["AutomationJobUnavailable"], identity);
        if (job.LastSuccessfulAnalysisRunId != run.Id)
            return Create(run.Id, AnalysisFreshnessStatus.Unknown, ["AnalysisRunJobBindingUnknown"], identity);
        if (string.IsNullOrWhiteSpace(job.ProcessedInputHash) ||
            string.IsNullOrWhiteSpace(job.ProcessedPolicyVersion) ||
            string.IsNullOrWhiteSpace(job.DesiredInputHash) ||
            string.IsNullOrWhiteSpace(job.DesiredPolicyVersion) ||
            string.IsNullOrWhiteSpace(run.PolicyVersion) || string.IsNullOrWhiteSpace(currentPolicyVersion))
            return Create(run.Id, AnalysisFreshnessStatus.Unknown, ["FreshnessIdentityIncomplete"], identity);
        if (!string.Equals(job.ProcessedPolicyVersion, run.PolicyVersion, StringComparison.Ordinal))
            return Create(run.Id, AnalysisFreshnessStatus.Unknown, ["ProcessedPolicyRunBindingUnknown"], identity);
        List<string> stale = [];
        if (!string.Equals(job.ProcessedInputHash, job.DesiredInputHash, StringComparison.Ordinal))
            stale.Add("SourceInputChanged");
        if (!string.Equals(job.ProcessedPolicyVersion, job.DesiredPolicyVersion, StringComparison.Ordinal))
            stale.Add("DesiredPolicyChanged");
        if (!string.Equals(job.DesiredPolicyVersion, currentPolicyVersion, StringComparison.Ordinal))
            stale.Add("CurrentPolicyChanged");
        return stale.Count > 0
            ? Create(run.Id, AnalysisFreshnessStatus.Stale, stale, identity)
            : Create(run.Id, AnalysisFreshnessStatus.Current, [], identity);
    }

    private static AnalysisFreshnessResult Create(long id, string status,
        IReadOnlyList<string> reasons, string identity) => new(id, status, reasons, identity);

    private static string Key(int canonicalWorkId, string language) => canonicalWorkId + "\n" + language;

    private sealed record RunIdentity(long Id, int CanonicalWorkId, string Language,
        string? PolicyVersion, string? SourceIdentityHash);
    private sealed record LatestIdentity(int CanonicalWorkId, string Language, long RunId);
    private sealed record JobIdentity(int CanonicalWorkId, string Language,
        long? LastSuccessfulAnalysisRunId, string? ProcessedInputHash, string? ProcessedPolicyVersion,
        string DesiredInputHash, string DesiredPolicyVersion);
}
