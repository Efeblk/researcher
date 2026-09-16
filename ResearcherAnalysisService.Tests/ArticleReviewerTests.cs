using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class ArticleReviewerTests
{
    [Fact]
    public async Task ReviewAsync_FourRoles_OmitsUnsupportedAndUncertainWithExactArithmetic()
    {
        ReviewArticleRequest request = Request("The study enrolled 40 participants and reports a mean of 4.2 units.");
        string sourceId = request.SourceSpans!.Single().SourceId;
        FixedReviewGenerator generator = new(role => new(role,
            [new("F1", role, role == "teaching" ? "teaching_adaptation" : "source_observation",
                "The study reports a value.", role == "teaching" ? "Ask learners to identify the reported value." : null,
                [sourceId])], "synthetic", ArticleReviewPrompt.Version));
        FixedReviewVerifier verifier = new((role, findings) => new(
            findings.Select(finding => new GeneratedArticleReviewVerdict(finding.FindingId,
                role switch { "quantitative" => "unsupported", "claim_evidence" => "uncertain", _ => "supported" },
                "Synthetic verdict.")).ToList(), "synthetic", ArticleReviewVerificationPrompt.Version));

        ArticleReviewReport report = await Create(generator, verifier).ReviewAsync(request, default);

        Assert.Equal(4, generator.Calls);
        Assert.Equal(4, verifier.Calls);
        Assert.Equal(4, report.Coverage.CandidateFindings);
        Assert.Equal(2, report.Coverage.SupportedFindings);
        Assert.Equal(1, report.Coverage.UnsupportedFindings);
        Assert.Equal(1, report.Coverage.UncertainFindings);
        Assert.Equal(2, report.Coverage.OmittedFindings);
        Assert.Equal(2, report.Reviews.Sum(review => review.Findings.Count));
        ArticleReviewEvidence evidence = report.Reviews.SelectMany(review => review.Findings).First().Evidence.Single();
        Assert.Equal(request.SourceSpans!.Single().Text, evidence.Quote);
        Assert.False(report.SourceFidelity!.FormulaAndTableLayoutVerified);
        Assert.False(report.SourceFidelity.FigureImageryAnalyzed);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("duplicate-source")]
    public async Task ReviewAsync_MalformedGeneratedFinding_FailsClosed(string mode)
    {
        ReviewArticleRequest request = Request("A source observation.");
        string id = request.SourceSpans!.Single().SourceId;
        FixedReviewGenerator generator = new(role =>
        {
            IReadOnlyList<string> ids = mode switch
            {
                "source" => ["unknown"],
                "duplicate-source" => [id, id],
                _ => [id]
            };
            return new(role, [new("F1", mode == "role" ? "wrong" : role,
                mode == "kind" ? "confirmed_error" : role == "teaching" ? "teaching_adaptation" : "source_observation",
                "Basis", role == "teaching" ? "Suggestion" : null, ids)],
                "synthetic", ArticleReviewPrompt.Version);
        });

        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Create(generator, new FixedReviewVerifier((_, findings) => Supported(findings))).ReviewAsync(request, default));
        Assert.Equal(AnalysisFailure.InvalidEvidence, exception.Reason);
        Assert.Equal("generation", exception.Stage);
        Assert.Equal("method", exception.Role);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("verdict")]
    public async Task ReviewAsync_MalformedVerdicts_FailsClosed(string mode)
    {
        ReviewArticleRequest request = Request("A source observation.");
        string id = request.SourceSpans!.Single().SourceId;
        FixedReviewGenerator generator = new(role => new(role,
            [new("F1", role, role == "teaching" ? "teaching_adaptation" : "source_observation",
                "Basis", role == "teaching" ? "Suggestion" : null, [id])], "synthetic", ArticleReviewPrompt.Version));
        FixedReviewVerifier verifier = new((_, findings) =>
        {
            IReadOnlyList<GeneratedArticleReviewVerdict> verdicts = mode switch
            {
                "missing" => [],
                "duplicate" => [new("F1", "supported", "ok"), new("F1", "supported", "ok")],
                "unknown" => [new("other", "supported", "ok")],
                _ => [new("F1", "accepted", "ok")]
            };
            return new(verdicts, "synthetic", ArticleReviewVerificationPrompt.Version);
        });

        await Assert.ThrowsAsync<InvalidAnalysisException>(() => Create(generator, verifier).ReviewAsync(request, default));
    }

    [Fact]
    public async Task ReviewAsync_AbstractPreservesPartialSourceCoverageAndNoFindingsIsExplicit()
    {
        IReadOnlyList<ArticlePage> pages = [new(null, "Only the saved abstract is available.")];
        ReviewArticleRequest request = new("en", "abstract", "hash", "abstract-v1",
            ArticleReviewer.DefaultPolicyVersion, pages, 1, true, "Only the abstract was analyzed.")
        { SourceSpans = ArticleSourceCatalog.Create(pages) };
        ArticleReviewReport report = await Create(
            new FixedReviewGenerator(role => new(role, [], "synthetic", ArticleReviewPrompt.Version)),
            new FixedReviewVerifier((_, findings) => Supported(findings))).ReviewAsync(request, default);

        Assert.Equal("no_supported_findings", report.Outcome);
        Assert.True(report.SourceCoverage.IsPartial);
        Assert.Equal("Only the abstract was analyzed.", report.SourceCoverage.ScopeReason);
        Assert.Equal(0, report.Coverage.CandidateFindings);
        Assert.Equal("no_supported_findings", report.Verification.Status);
    }

    [Fact]
    public async Task ReviewAsync_OversizedSource_RejectsBeforeModelCall()
    {
        ReviewArticleRequest request = Request(new string('x', 6000));
        FixedReviewGenerator generator = new(role => new(role, [], "synthetic", ArticleReviewPrompt.Version));

        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() => Create(generator,
            new FixedReviewVerifier((_, findings) => Supported(findings)),
            new AiOptions { ArticleReviewMaximumInputBytes = 4096 }).ReviewAsync(request, default));
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task ReviewAsync_ConfiguredEngine_ReturnsReport()
    {
        ReviewArticleRequest request = Request("A source observation.");
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            reviewGenerator: new FixedReviewGenerator(role => new(role, [], "synthetic", ArticleReviewPrompt.Version)),
            reviewVerifier: new FixedReviewVerifier((_, findings) => Supported(findings)));

        ArticleReviewReport report = await host.InvokeAsync<ArticleReviewer, ArticleReviewReport>(
            reviewer => reviewer.ReviewAsync(request, default));
        Assert.Equal(ArticleReviewer.DefaultPolicyVersion, report.PolicyVersion);
    }

    [Fact]
    public async Task ReviewAsync_OverallTimeoutStopsBeforeLaterSpecialists()
    {
        BlockingReviewGenerator generator = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            settings: new Dictionary<string, string?> { ["Ai:ArticleReviewTimeoutSeconds"] = "1" },
            reviewGenerator: generator,
            reviewVerifier: new FixedReviewVerifier((_, findings) => Supported(findings)));

        ArticleReviewTimedOutException error = await Assert.ThrowsAsync<ArticleReviewTimedOutException>(() =>
            host.InvokeAsync<ArticleReviewer, ArticleReviewReport>(reviewer =>
                reviewer.ReviewAsync(Request("A source observation."), default)));
        Assert.Equal(1, generator.Calls);
        Assert.Equal("generation", error.Stage);
        Assert.Equal("method", error.Role);
    }

    [Fact]
    public async Task ReviewAsync_InvalidJsonPreservesGenerationRoleDetail()
    {
        FixedReviewGenerator generator = new(_ =>
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            reviewGenerator: generator,
            reviewVerifier: new FixedReviewVerifier((_, findings) => Supported(findings)));

        InvalidAnalysisException error = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            host.InvokeAsync<ArticleReviewer, ArticleReviewReport>(reviewer =>
                reviewer.ReviewAsync(Request("A source observation."), default)));
        Assert.Equal(AnalysisFailure.InvalidJson, error.Reason);
        Assert.Equal("generation", error.Stage);
        Assert.Equal("method", error.Role);
    }

    [Fact]
    public async Task ReviewAsync_InvalidEvidencePreservesGenerationRoleDetail()
    {
        FixedReviewGenerator generator = new(role => new(role,
            [new("F1", role, "source_observation", "Basis.", null, ["unknown-source"])],
            "synthetic", ArticleReviewPrompt.Version));
        FixedReviewVerifier verifier = new((_, findings) => Supported(findings));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            reviewGenerator: generator,
            reviewVerifier: verifier);

        InvalidAnalysisException error = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            host.InvokeAsync<ArticleReviewer, ArticleReviewReport>(reviewer =>
                reviewer.ReviewAsync(Request("A source observation."), default)));
        Assert.Equal(AnalysisFailure.InvalidEvidence, error.Reason);
        Assert.Equal("generation", error.Stage);
        Assert.Equal("method", error.Role);
        Assert.Equal(0, verifier.Calls);
    }

    private static ReviewArticleRequest Request(string text)
    {
        IReadOnlyList<ArticlePage> pages = [new(1, text)];
        return new("en", "pdf", "hash", "pdf-v1", ArticleReviewer.DefaultPolicyVersion,
            pages, 1, false, null) { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    private static ArticleReviewer Create(IArticleReviewGenerator generator, IArticleReviewVerifier verifier,
        AiOptions? options = null) => new(generator, verifier, Options.Create(options ?? new AiOptions()));

    private static GeneratedArticleReviewVerification Supported(IReadOnlyList<GeneratedArticleReviewFinding> findings) =>
        new(findings.Select(finding => new GeneratedArticleReviewVerdict(finding.FindingId, "supported", "ok")).ToList(),
            "synthetic", ArticleReviewVerificationPrompt.Version);

    private sealed class FixedReviewGenerator(Func<string, GeneratedArticleReviewPass> create) : IArticleReviewGenerator
    {
        public int Calls { get; private set; }
        public Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language, string sourceKind,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(create(role));
        }
    }

    private sealed class FixedReviewVerifier(
        Func<string, IReadOnlyList<GeneratedArticleReviewFinding>, GeneratedArticleReviewVerification> create)
        : IArticleReviewVerifier
    {
        public int Calls { get; private set; }
        public Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
            IReadOnlyList<GeneratedArticleReviewFinding> findings, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(create(role, findings));
        }
    }

    private sealed class BlockingReviewGenerator : IArticleReviewGenerator
    {
        public int Calls { get; private set; }
        public async Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language, string sourceKind,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new(role, [], "synthetic", ArticleReviewPrompt.Version);
        }
    }
}
