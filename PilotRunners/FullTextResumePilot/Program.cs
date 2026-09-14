using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace FullTextResumePilot;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        string command = args.FirstOrDefault() ?? "preflight";
        if (command == "preflight")
        {
            PreflightResult result = RunPreflight(ResolveSourceDirectory(args));
            string outputPath = Path.Combine(FindRepositoryRoot(), "PilotRunners", "FullTextResumePilot",
                "preflight-20260912-result.json");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return result.Passed ? 0 : 1;
        }

        if (command == "live")
        {
            Console.Error.WriteLine("The frozen 20260912 live command is retired; use acceptance-live with a new release token.");
            return 2;
        }

        if (command == "acceptance-preflight")
        {
            string sourceDirectory = ResolveSourceDirectory(args);
            PreflightResult source = RunPreflight(sourceDirectory);
            CitationProbePreflight probes = LivePilot.ValidateCitationProbePlan(sourceDirectory);
            AcceptancePreflightResult result = new(DateTime.UtcNow, source.Passed && probes.Passed,
                LivePilot.AcceptanceRunId, source, probes);
            string outputPath = Path.Combine(FindRepositoryRoot(), "PilotRunners", "FullTextResumePilot",
                "preflight-fulltext-citation-alignment-pilot-20260912.json");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return result.Passed ? 0 : 1;
        }

        if (command == "acceptance-live")
        {
            if (!args.Contains("--execute-paid-pilot", StringComparer.Ordinal) ||
                Environment.GetEnvironmentVariable("FULLTEXT_CITATION_ALIGNMENT_PILOT_RELEASE") !=
                    "ROOT_RELEASED_FULLTEXT_CITATION_ALIGNMENT_20260912")
            {
                Console.Error.WriteLine("Paid acceptance execution is locked. Supply the explicit switch and exact root release value.");
                return 2;
            }
            return await LivePilot.RunAsync(ResolveSourceDirectory(args),
                args.Contains("--resume", StringComparer.Ordinal));
        }

        if (command == "audit-result")
            return await LivePilot.AuditFrozenResultAsync();

        Console.Error.WriteLine("Usage: FullTextResumePilot [preflight|audit-result|acceptance-preflight|acceptance-live --execute-paid-pilot [--resume]]");
        return 2;
    }

    internal static PreflightResult RunPreflight(string sourceDirectory)
    {
        SourceDefinition[] sources =
        [
            new("adam", Path.Combine(sourceDirectory, "adam-v1-fulltext.pdf"), 555695,
                "935a5a15616961aff21529d86a754570028843407adfe858f1d18584b84293a7",
                9, 30904, 64, "ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3"),
            new("football", Path.Combine(sourceDirectory, "football-fulltext.pdf"), 3282662,
                "9c3d977e50059edce06618dc5bc5454b393ff50d9539d2400aafb252cc4dae58",
                13, null, 136, null)
        ];
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions
        {
            MaximumPages = 40,
            MaximumExtractedCharacters = 160000,
            OcrEnabled = false
        }));
        IOptions<AiOptions> aiOptions = Options.Create(new AiOptions
        {
            ArticleProvider = "Gemini",
            ArticleModel = "gemini-3.8-flash",
            ArticleVerifierModel = "gemini-3.8-flash",
            ArticleContextTokens = 131072,
            ArticleMaxOutputTokens = 8192,
            ArticleVerifierMaxOutputTokens = 8192,
            ArticleGenerationThinkingLevel = "high",
            ArticleVerifierThinkingLevel = "high",
            ArticleReviewMaximumInputBytes = 100000
        });
        UnusedReviewProvider unused = new();
        ArticleReviewer reviewer = new(unused, unused, aiOptions);
        ArticleReviewStageExecutor stageExecutor = new(reviewer, unused, unused,
            new ArticleReviewDispatchContext(), aiOptions);
        List<SourcePreflight> checks = [];
        foreach (SourceDefinition source in sources)
            checks.Add(CheckSource(source, extractor, reviewer, stageExecutor));
        return new(DateTime.UtcNow, checks.All(value => value.Passed),
            "No provider or public network request is made by preflight.", checks);
    }

    private static SourcePreflight CheckSource(SourceDefinition source, ArticlePdfExtractor extractor,
        ArticleReviewer reviewer, ArticleReviewStageExecutor stageExecutor)
    {
        byte[] bytes = File.ReadAllBytes(source.Path);
        string pdfHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        SummarizeArticleRequest extracted = extractor.Extract(bytes, "en");
        string canonicalPages = JsonSerializer.Serialize(extracted.Pages,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPages)))
            .ToLowerInvariant();
        IReadOnlyList<ArticleSourceSpan> sourceSpans = extracted.SourceSpans ??
            throw new InvalidOperationException("Native extraction did not create source spans.");
        ReviewArticleRequest review = new("en", "pdf", sourceHash, ArticlePdfExtractor.Version,
            ArticleReviewer.DefaultPolicyVersion, extracted.Pages, extracted.TotalSourcePages, false, null)
        {
            SourceSpans = sourceSpans
        };
        reviewer.ValidateRequest(review);
        int sourceBytes = sourceSpans.Sum(span => Encoding.UTF8.GetByteCount(span.SourceId) +
            Encoding.UTF8.GetByteCount(span.Text) + 80);
        bool exactOffsets = sourceSpans.All(span => extracted.Pages.Single(page =>
                page.PageNumber == span.PageNumber).Text[span.StartOffset..span.EndOffset] == span.Text) &&
            ArticleSourceCatalog.IsValid(extracted.Pages, sourceSpans, "pdf");
        List<StagePreflight> stages = [];
        foreach (string role in ArticleReviewer.Roles)
        {
            ArticleReviewStageQuote generation = stageExecutor.Quote(
                new(ArticleReviewStageKinds.Generation, role, review));
            ArticleReviewCandidateFinding[] findings = Enumerable.Range(1, 3).Select(index =>
                new ArticleReviewCandidateFinding($"offline-{role}-{index}", role, "review_question",
                    "Offline quote preflight uses a bounded synthetic candidate.",
                    "A reviewer could inspect this cited source span.",
                    [sourceSpans[(index - 1) % sourceSpans.Count].SourceId])).ToArray();
            ArticleReviewStageQuote verification = stageExecutor.Quote(
                new(ArticleReviewStageKinds.Verification, role, review) { Findings = findings });
            stages.Add(new(role, generation.MaximumChargeUsd, verification.MaximumChargeUsd));
        }
        bool expected = bytes.Length == source.ExpectedBytes && pdfHash == source.ExpectedPdfHash &&
            extracted.Pages.Count == source.ExpectedPages &&
            (!source.ExpectedCharacters.HasValue || extracted.Pages.Sum(page => page.Text.Length) == source.ExpectedCharacters) &&
            sourceSpans.Count == source.ExpectedSpans &&
            (source.ExpectedSourceHash is null || sourceHash == source.ExpectedSourceHash);
        return new(source.Name, expected && exactOffsets && sourceBytes <= 100000,
            bytes.Length, pdfHash, extracted.Pages.Count, extracted.Pages.Sum(page => page.Text.Length),
            sourceSpans.Count, sourceBytes, sourceHash, exactOffsets,
            sourceBytes <= 100000, stages.Sum(value => value.GenerationMaximumChargeUsd +
                value.VerificationMaximumChargeUsd), stages);
    }

    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcademicCollectorDemo.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    internal static string ResolveSourceDirectory(string[] args)
    {
        int option = Array.IndexOf(args, "--source-directory");
        string? value = option >= 0 && option + 1 < args.Length ? args[option + 1] :
            Environment.GetEnvironmentVariable("FULLTEXT_PILOT_SOURCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                "Set FULLTEXT_PILOT_SOURCE_DIRECTORY or pass --source-directory with the folder containing the two pilot PDFs.");
        return Path.GetFullPath(value);
    }

    private sealed record SourceDefinition(string Name, string Path, int ExpectedBytes,
        string ExpectedPdfHash, int ExpectedPages, int? ExpectedCharacters, int ExpectedSpans,
        string? ExpectedSourceHash);

    private sealed class UnusedReviewProvider : IArticleReviewGenerator, IArticleReviewVerifier
    {
        public Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language,
            string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Offline preflight cannot dispatch a model call.");

        public Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
            IReadOnlyList<GeneratedArticleReviewFinding> findings,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Offline preflight cannot dispatch a model call.");
    }
}

public sealed record PreflightResult(DateTime CheckedAtUtc, bool Passed, string NetworkPolicy,
    IReadOnlyList<SourcePreflight> Sources);

public sealed record SourcePreflight(string Name, bool Passed, int PdfBytes, string PdfSha256,
    int Pages, int Utf16Characters, int Spans, int SourcePayloadBytes, string SourceHash,
    bool ExactSpanOffsets, bool FitsUnchangedSourceLimit, decimal SampleEightStageReservationUsd,
    IReadOnlyList<StagePreflight> Stages);

public sealed record StagePreflight(string Role, decimal GenerationMaximumChargeUsd,
    decimal VerificationMaximumChargeUsd);

public sealed record AcceptancePreflightResult(DateTime CheckedAtUtc, bool Passed, string RunId,
    PreflightResult SourcePreflight, CitationProbePreflight CitationProbePreflight);
