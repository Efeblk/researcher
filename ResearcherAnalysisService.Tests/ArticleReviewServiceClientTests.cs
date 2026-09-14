using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Products.ArticleReviews;

namespace ResearcherAnalysisService.Tests;

public sealed class ArticleReviewServiceClientTests
{
    private const string Model = "synthetic-review-model";

    [Fact]
    public async Task ReviewAsync_ValidGeneratedFinding_ReturnsCanonicalEvidence()
    {
        ReviewArticleRequest request = Request();
        ArticleSourceSpan span = request.SourceSpans!.Single();
        StubReviewGenerator generator = new((role, _, _, _, _) => Task.FromResult(
            Pass(role, role == "method"
                ? [new("F1", role, "source_observation", "Reported method.", null, [span.SourceId])]
                : [])));
        StubReviewVerifier verifier = new((_, _, findings, _, _) => Task.FromResult(
            new GeneratedArticleReviewVerification(
                findings.Select(finding => new GeneratedArticleReviewVerdict(
                    finding.FindingId, "supported", "Supported by the cited source.")).ToArray(),
                Model, ArticleReviewVerificationPrompt.Version)));

        ArticleReviewReport report = await Client(generator, verifier).ReviewAsync(request, default);

        ArticleReviewFinding finding = Assert.Single(report.Reviews[0].Findings);
        Assert.Equal("method:F1", finding.FindingId);
        Assert.Equal(span.SourceId, Assert.Single(finding.Evidence).SourceId);
        Assert.Equal("automatically_checked", report.Outcome);
    }

    [Fact]
    public async Task ReviewAsync_GeneratorFailure_PreservesTypedStageAndRoleContext()
    {
        StubReviewGenerator generator = new((_, _, _, _, _) =>
            throw new InvalidAnalysisException(AnalysisFailure.OutputLimit));

        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Client(generator, NeverVerifier()).ReviewAsync(Request(), default));

        Assert.Equal(AnalysisFailure.OutputLimit, exception.Reason);
        Assert.Equal("generation", exception.Stage);
        Assert.Equal("method", exception.Role);
        Assert.DoesNotContain("provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewAsync_UnknownSourceId_FailsBeforeVerification()
    {
        StubReviewVerifier verifier = NeverVerifier();
        StubReviewGenerator generator = new((role, _, _, _, _) => Task.FromResult(
            Pass(role, role == "method"
                ? [new("F1", role, "source_observation", "Reported method.", null, ["unknown-source"])]
                : [])));

        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Client(generator, verifier).ReviewAsync(Request(), default));

        Assert.Equal(AnalysisFailure.InvalidEvidence, exception.Reason);
        Assert.Equal("generation", exception.Stage);
        Assert.Equal(0, verifier.Calls);
    }

    [Theory]
    [InlineData("null-review")]
    [InlineData("null-finding")]
    [InlineData("null-evidence")]
    [InlineData("null-source-id")]
    [InlineData("wrong-quote")]
    [InlineData("oversized-counter")]
    public void Validate_MaliciousNestedReport_FailsClosed(string mode)
    {
        ReviewArticleRequest request = Request();
        ArticleReviewReport report = Report(request);
        ArticleSpecialistReview[] reviews = report.Reviews.ToArray();
        ArticleReviewFinding[] findings = reviews[0].Findings.ToArray();
        ArticleReviewEvidence[] evidence = findings[0].Evidence.ToArray();
        switch (mode)
        {
            case "null-review":
                reviews[0] = null!;
                break;
            case "null-finding":
                findings[0] = null!;
                reviews[0] = reviews[0] with { Findings = findings };
                break;
            case "null-evidence":
                evidence[0] = null!;
                findings[0] = findings[0] with { Evidence = evidence };
                reviews[0] = reviews[0] with { Findings = findings };
                break;
            case "null-source-id":
                evidence[0] = evidence[0] with { SourceId = null! };
                findings[0] = findings[0] with { Evidence = evidence };
                reviews[0] = reviews[0] with { Findings = findings };
                break;
            case "wrong-quote":
                evidence[0] = evidence[0] with { Quote = "Invented quote." };
                findings[0] = findings[0] with { Evidence = evidence };
                reviews[0] = reviews[0] with { Findings = findings };
                break;
            case "oversized-counter":
                report = report with { Coverage = report.Coverage with { CandidateFindings = 1_000_000 } };
                break;
        }
        report = report with { Reviews = reviews };

        Assert.Throws<JsonException>(() => ArticleReviewServiceClient.Validate(report, request));
    }

    [Fact]
    public async Task ReviewAsync_SourceBudgetExceeded_DoesNotCallGenerator()
    {
        IReadOnlyList<ArticlePage> pages = [new(1, new string('a', 5000))];
        ReviewArticleRequest request = new("en", "pdf", "hash", "pdf-v1",
            ArticleReviewer.DefaultPolicyVersion, pages, 1, false, null)
        {
            SourceSpans = ArticleSourceCatalog.Create(pages)
        };
        StubReviewGenerator generator = new((_, _, _, _, _) =>
            throw new InvalidOperationException("Generator must not run over budget."));
        AiOptions options = new() { ArticleReviewMaximumInputBytes = 4096 };

        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() =>
            Client(generator, NeverVerifier(), options).ReviewAsync(request, default));
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task ReviewAsync_CallerCancellation_PropagatesToGenerator()
    {
        StubReviewGenerator generator = new(async (_, _, _, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(generator, NeverVerifier()).ReviewAsync(Request(), cancellation.Token));
    }

    private static ArticleReviewServiceClient Client(StubReviewGenerator generator,
        StubReviewVerifier verifier, AiOptions? options = null) => new(
        new ArticleReviewer(generator, verifier, Options.Create(options ?? new AiOptions())), null!);

    private static StubReviewVerifier NeverVerifier() => new((_, _, _, _, _) =>
        throw new InvalidOperationException("Verifier must not run."));

    private static GeneratedArticleReviewPass Pass(string role,
        IReadOnlyList<GeneratedArticleReviewFinding> findings) =>
        new(role, findings, Model, ArticleReviewPrompt.Version);

    private static ReviewArticleRequest Request()
    {
        IReadOnlyList<ArticlePage> pages = [new(1, "A directly reported method.")];
        return new("en", "pdf", "hash", "pdf-v1", ArticleReviewer.DefaultPolicyVersion,
            pages, 1, false, null) { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    private static ArticleReviewReport Report(ReviewArticleRequest request)
    {
        ArticleSourceSpan span = request.SourceSpans!.Single();
        return new(request.Language, request.SourceKind, request.SourceHash, request.ExtractionVersion,
            request.PolicyVersion, "automatically_checked", new(1, 1, 1, false, null),
            new(4, 4, 1, 1, 1, 0, 0, 0, []),
            [
                new("method", "automatically_checked",
                    [new("method:F1", "method", "source_observation", "Reported method.", null,
                        [new(span.SourceId, span.PageNumber, span.StartOffset, span.EndOffset, span.Text)])]),
                new("quantitative", "no_supported_findings", []),
                new("claim_evidence", "no_supported_findings", []),
                new("teaching", "no_supported_findings", [])
            ],
            Model, ArticleReviewPrompt.Version,
            new("automatically_checked", Model, ArticleReviewVerificationPrompt.Version, true,
                "Automatic checking has limitations."))
        {
            SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
        };
    }

    private sealed class StubReviewGenerator(
        Func<string, string, string, IReadOnlyList<ArticleSourceSpan>, CancellationToken,
            Task<GeneratedArticleReviewPass>> generate) : IArticleReviewGenerator
    {
        public int Calls { get; private set; }

        public Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language,
            string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            Calls++;
            return generate(role, language, sourceKind, sourceSpans, cancellationToken);
        }
    }

    private sealed class StubReviewVerifier(
        Func<string, string, IReadOnlyList<GeneratedArticleReviewFinding>,
            IReadOnlyList<ArticleSourceSpan>, CancellationToken,
            Task<GeneratedArticleReviewVerification>> verify) : IArticleReviewVerifier
    {
        public int Calls { get; private set; }

        public Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
            IReadOnlyList<GeneratedArticleReviewFinding> findings,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            Calls++;
            return verify(role, language, findings, sourceSpans, cancellationToken);
        }
    }
}
