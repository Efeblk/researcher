using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ServiceAcceptancePilot;

internal static class RetainedV4RetrievalPreflight
{
    private const string DatabaseName = "AcademicFinalServiceAcceptance_575d8936376243d49ca269e97ec0aa9d";
    private const string PersonelId = "service-acceptance-faculty";
    private const string AdamHash = "ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3";
    private const string FootballHash = "c2f7f4ac5265813d06ac7f3776abc9b7dcdfe0c21efc1b943fc4059afab950ab";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync()
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine(repositoryRoot, "docs", "service-acceptance-20260914-v5-preflight",
            "retrieval-final-method-findings.json");
        if (File.Exists(path))
            throw new InvalidOperationException($"Retrieval preflight artifact already exists: {path}");

        (_, string connection) = FinalAcceptanceDatabase.Connections(DatabaseName);
        await using var database = FinalAcceptanceDatabase.Open(connection);
        var before = await SourceIdentitiesAsync(database);
        AcademicEvidenceSearchService search = new(database,
            Options.Create(new ArticleSummaryAutomationOptions { PolicyVersion = "article-summary-v5" }));

        AcademicEvidenceSearchResponse methods = await SearchAsync(search, 1,
            "Bu makalenin yöntem ve sınırlılıklarını kaynaklarıyla açıkla; kendi çalışmamda hangi koşulları kontrol etmeliyim?");
        AcademicEvidenceSearchResponse teaching = await SearchAsync(search, 2,
            "Bu makalenin yöntem ve bulgular bölümünden dersimde kullanabileceğim bir örnek ve bir tartışma sorusu hazırla.");
        AcademicEvidenceSearchResponse issues = await SearchAsync(search, 1,
            "Bu makalenin yöntem ve bulgularında yeniden kontrol edilmesi gereken noktaları, kesin hata ile belirsizliği ayırarak göster.");
        var after = await SourceIdentitiesAsync(database);

        bool sourceHashesIntact = before.SequenceEqual(after) && before.Count == 2 &&
            before.Any(value => value.CanonicalWorkId == 1 && value.ExtractedTextHash == AdamHash) &&
            before.Any(value => value.CanonicalWorkId == 2 && value.ExtractedTextHash == FootballHash);
        bool methodsGate = methods.Hits.Any(hit => hit.ArticleSourceSpanId == 29 &&
                hit.ExactText.Contains("bounded gradients", StringComparison.Ordinal)) &&
            methods.Hits.Any(hit => hit.ArticleSourceSpanId == 43 &&
                hit.ExactText.Contains("does not apply", StringComparison.Ordinal)) &&
            methods.Hits.Any(hit => HasIntent(hit, "methods"));
        bool teachingGate = teaching.Hits.Any(hit => hit.ArticleSourceSpanId == 152 &&
                HasIntent(hit, "methods")) && teaching.Hits.Any(hit => hit.ArticleSourceSpanId == 146 &&
                HasIntent(hit, "findings"));
        bool issuesGate = issues.Hits.Any(hit => hit.ArticleSourceSpanId == 29) &&
            issues.Hits.Any(hit => hit.ArticleSourceSpanId == 43) &&
            issues.Hits.Any(hit => HasIntent(hit, "methods")) &&
            issues.Hits.Any(hit => HasIntent(hit, "findings"));
        bool noTrackedChanges = !database.ChangeTracker.HasChanges();
        bool passed = sourceHashesIntact && methodsGate && teachingGate && issuesGate && noTrackedChanges;

        object artifact = new
        {
            runId = "service-acceptance-20260914-v5-preflight",
            sourceLifecycle = "service-acceptance-20260914-v4",
            DatabaseName,
            executedAtUtc = DateTimeOffset.UtcNow,
            paidProviderCalls = 0,
            readOnly = true,
            catalogVersion = AcademicEvidenceSearchService.CatalogVersion,
            policyVersion = "article-summary-v5",
            take = 10,
            passed,
            gates = new { sourceHashesIntact, methodsGate, teachingGate, issuesGate, noTrackedChanges },
            sourceIdentitiesBefore = before,
            sourceIdentitiesAfter = after,
            methods,
            teaching,
            issues
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(artifact, JsonOptions) + Environment.NewLine);
        File.Move(temporary, path);
        Console.WriteLine(passed ? $"RETRIEVAL_PREFLIGHT_OK {path}" : $"RETRIEVAL_PREFLIGHT_FAILED {path}");
        return passed ? 0 : 1;
    }

    private static Task<AcademicEvidenceSearchResponse> SearchAsync(AcademicEvidenceSearchService search,
        int canonicalWorkId, string query) => search.SearchAsync(PersonelId, new()
        {
            Query = query,
            CanonicalWorkIds = [canonicalWorkId],
            Take = 10
        }, default);

    private static bool HasIntent(AcademicEvidenceSearchHitDto hit, string intent) =>
        hit.MatchProvenance?.Any(value => value.Kind == "source_text_intent" &&
            value.Section == intent) == true;

    private static async Task<List<SourceIdentity>> SourceIdentitiesAsync(
        AcademicCollectorDemo.Modules.AcademicPerformance.Data.AcademicDbContext database) =>
        await database.ArticleSourceSnapshots.AsNoTracking().OrderBy(value => value.Id)
            .Select(value => new SourceIdentity(value.Id, value.CanonicalWorkId, value.ExtractedTextHash,
                value.SourceKind, value.ExtractionVersion, value.Spans.Count))
            .ToListAsync();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcademicCollectorDemo.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed record SourceIdentity(long Id, int CanonicalWorkId, string ExtractedTextHash,
        string SourceKind, string ExtractionVersion, int SpanCount);
}
