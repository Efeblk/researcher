using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Tests;

public sealed class ArticleSummarizerTests
{
    [Fact]
    public async Task SummarizeAsync_SupportedClaim_ResolvesImmutableSource()
    {
        SummarizeArticleRequest request = Request("Model B trained for 3.5 days and scored 28.4 BLEU.");
        ArticleSourceSpan span = request.SourceSpans!.Single();
        FixedGenerator generator = new(new([new("c1", "Model B scored 28.4 BLEU.", [span.SourceId])], [], [], [], []));
        ArticleSummarizer summarizer = Create(generator, new FixedVerifier("supported", "Numbers and system match."));

        ArticleSummaryReport report = await summarizer.SummarizeAsync(request, default);

        ArticleClaim claim = Assert.Single(report.Sections.Purpose);
        ArticleEvidence evidence = Assert.Single(claim.Evidence);
        Assert.Equal(span.Text, evidence.Quote);
        Assert.Equal(span.SourceId, evidence.SourceId);
        Assert.Equal(span.StartOffset, evidence.StartOffset);
        Assert.Equal("automatically_checked", report.Verification!.Status);
        Assert.Equal(1, report.Coverage.SupportedClaims);
    }

    [Fact]
    public async Task SummarizeAsync_UnknownOrCrossChunkSourceId_Rejects()
    {
        SummarizeArticleRequest request = Request("A supported source sentence.");
        FixedGenerator generator = new(new([new("c1", "claim", ["src-other"])], [], [], [], []));

        await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Create(generator, new FixedVerifier("supported", "ok")).SummarizeAsync(request, default));
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("uncertain")]
    public async Task SummarizeAsync_NonSupportedVerdict_OmitsClaimAndReportsInsufficient(string verdict)
    {
        SummarizeArticleRequest request = Request("An observational association was reported.");
        string id = request.SourceSpans!.Single().SourceId;
        ArticleSummarizer summarizer = Create(
            new FixedGenerator(new([new("c1", "The exposure caused the result.", [id])], [], [], [], [])),
            new FixedVerifier(verdict, "Causality is not established."));

        ArticleSummaryReport report = await summarizer.SummarizeAsync(request, default);

        Assert.Empty(report.Sections.Purpose);
        Assert.Equal("insufficient_evidence", report.Verification!.Status);
        Assert.Equal(1, report.Coverage.SelectedClaimsOmitted);
        Assert.NotEmpty(report.Coverage.OmissionReasons!);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public async Task SummarizeAsync_InvalidVerdictCardinality_FailsClosed(string mode)
    {
        SummarizeArticleRequest request = Request("A supported source sentence.");
        string id = request.SourceSpans!.Single().SourceId;
        ArticleSummarizer summarizer = Create(
            new FixedGenerator(new([new("c1", "claim", [id])], [], [], [], [])), new InvalidVerifier(mode));

        await Assert.ThrowsAsync<InvalidAnalysisException>(() => summarizer.SummarizeAsync(request, default));
    }

    [Fact]
    public async Task SummarizeAsync_AdaptiveFallback_PreservesEveryCanonicalSpan()
    {
        string text = string.Concat(Enumerable.Repeat("T\u00fcrk\u00e7e \U0001F9EA result. ", 100));
        SummarizeArticleRequest request = Request(text);
        OverflowGenerator generator = new();
        ArticleSummarizer summarizer = Create(generator, new FixedVerifier("supported", "Directly stated."),
            new AiOptions { ArticleFallbackChunkBytes = 1000 });

        ArticleSummaryReport report = await summarizer.SummarizeAsync(request, default);

        Assert.True(report.Coverage.TotalChunks > 1);
        Assert.Equal(request.SourceSpans, generator.SuccessfulSpans);
    }

    [Fact]
    public async Task SummarizeAsync_VerifierBatchOverflows_SplitsVerificationWithoutRegeneratingArticle()
    {
        SummarizeArticleRequest request = Request("One source supports several concise claims.");
        string id = request.SourceSpans!.Single().SourceId;
        GeneratedArticleClaim[] first = Enumerable.Range(1, 3).Select(i => new GeneratedArticleClaim($"p{i}", $"purpose {i}", [id])).ToArray();
        GeneratedArticleClaim[] second = Enumerable.Range(1, 3).Select(i => new GeneratedArticleClaim($"m{i}", $"method {i}", [id])).ToArray();
        CountingGenerator generator = new(new(first, second, [], [], []));
        SplittingVerifier verifier = new();

        ArticleSummaryReport report = await Create(generator, verifier).SummarizeAsync(request, default);

        Assert.Equal(1, generator.Calls);
        Assert.Equal(6, report.Coverage.SupportedClaims);
        Assert.True(verifier.Calls > 2);
    }

    [Fact]
    public async Task SummarizeAsync_SingleVerifierBudgetFailure_OmitsClaimWithoutRegeneratingArticle()
    {
        SummarizeArticleRequest request = Request("A source sentence.");
        string id = request.SourceSpans!.Single().SourceId;
        CountingGenerator generator = new(new([new("c1", "claim", [id])], [], [], [], []));

        ArticleSummaryReport report = await Create(generator, new AlwaysOverflowVerifier()).SummarizeAsync(request, default);

        Assert.Equal(1, generator.Calls);
        Assert.Equal("insufficient_evidence", report.Verification!.Status);
        Assert.Equal(1, report.Coverage.BudgetUnverifiedClaims);
        Assert.Empty(report.Sections.Purpose);
    }

    [Fact]
    public void SourceCatalog_StableIdsOffsetsAndFullReconstruction()
    {
        IReadOnlyList<ArticlePage> pages = [new(1, "First sentence.  \nSecond sentence.")];
        IReadOnlyList<ArticleSourceSpan> first = ArticleSourceCatalog.Create(pages, 18);
        IReadOnlyList<ArticleSourceSpan> second = ArticleSourceCatalog.Create(pages, 18);

        Assert.Equal(first, second);
        Assert.Equal(pages[0].Text, string.Concat(first.Select(x => x.Text)));
        Assert.True(ArticleSourceCatalog.IsValid(pages, ArticleSourceCatalog.Create(pages), "pdf"));
        var complete = ArticleSourceCatalog.Create(pages, 10);
        var incomplete = complete.Skip(1).ToList();
        Assert.NotEmpty(incomplete);
        Assert.False(ArticleSourceCatalog.IsValid(pages, incomplete, "pdf"));
    }

    [Fact]
    public async Task SummarizeAsync_HtmlSource_AcceptsNullPageAndReportsExtractionMethod()
    {
        IReadOnlyList<ArticlePage> pages = [new(null, "HTML article body with a supported result.")];
        SummarizeArticleRequest request = new("en", "html", "hash", "html-v1", pages, 1, false, null)
            { SourceSpans = ArticleSourceCatalog.Create(pages) };
        string id = request.SourceSpans.Single().SourceId;

        ArticleSummaryReport report = await Create(
            new FixedGenerator(new([new("c1", "Supported result.", [id])], [], [], [], [])),
            new FixedVerifier("supported", "Directly stated.")).SummarizeAsync(request, default);

        Assert.Equal("html", report.ExtractionMethod);
        Assert.Null(Assert.Single(Assert.Single(report.Sections.Purpose).Evidence).PageNumber);
    }

    [Theory]
    [InlineData("html", 1)]
    [InlineData("abstract", 1)]
    [InlineData("unknown", null)]
    public async Task SummarizeAsync_InvalidSourceKindOrFakePageNumber_Rejects(string sourceKind, int? pageNumber)
    {
        IReadOnlyList<ArticlePage> pages = [new(pageNumber, "Source text")];
        SummarizeArticleRequest request = new("en", sourceKind, "hash", "v1", pages, 1, false, null)
            { SourceSpans = ArticleSourceCatalog.Create(pages) };

        await Assert.ThrowsAsync<BadHttpRequestException>(() => Create(
            new FixedGenerator(new([], [], [], [], [])), new FixedVerifier("supported", "ok"))
            .SummarizeAsync(request, default));
    }

    private static SummarizeArticleRequest Request(string text)
    {
        IReadOnlyList<ArticlePage> pages = [new(1, text)];
        return new("en", "pdf", "hash", "v2", pages, 1, false, null)
            { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    private static ArticleSummarizer Create(IArticleSummaryGenerator generator, IArticleClaimVerifier verifier,
        AiOptions? options = null) => new(generator, verifier, Options.Create(options ?? new AiOptions()));

    private sealed class FixedGenerator(GeneratedArticleSections sections) : IArticleSummaryGenerator
    {
        public Task<GeneratedArticleChunk> GenerateAsync(string language, string sourceKind,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken) =>
            Task.FromResult(new GeneratedArticleChunk(sections, "synthetic", ArticleSummaryPrompt.Version));
    }

    private sealed class CountingGenerator(GeneratedArticleSections sections) : IArticleSummaryGenerator
    {
        public int Calls { get; private set; }
        public Task<GeneratedArticleChunk> GenerateAsync(string language, string sourceKind,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new GeneratedArticleChunk(sections, "synthetic", ArticleSummaryPrompt.Version));
        }
    }

    private sealed class SplittingVerifier : IArticleClaimVerifier
    {
        public int Calls { get; private set; }
        public Task<GeneratedVerificationBatch> VerifyAsync(string language, IReadOnlyList<GeneratedArticleClaim> claims,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            Calls++;
            if (claims.Count > 2) throw new AnalysisInputTooLargeException();
            return Task.FromResult(new GeneratedVerificationBatch(
                claims.Select(x => new GeneratedClaimVerdict(x.ClaimId, "supported", "ok")).ToList(),
                "synthetic", ArticleVerificationPrompt.Version));
        }
    }

    private sealed class AlwaysOverflowVerifier : IArticleClaimVerifier
    {
        public Task<GeneratedVerificationBatch> VerifyAsync(string language, IReadOnlyList<GeneratedArticleClaim> claims,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken) =>
            throw new AnalysisInputTooLargeException();
    }

    private sealed class FixedVerifier(string verdict, string reason) : IArticleClaimVerifier
    {
        public Task<GeneratedVerificationBatch> VerifyAsync(string language, IReadOnlyList<GeneratedArticleClaim> claims,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken) =>
            Task.FromResult(new GeneratedVerificationBatch(claims.Select(x => new GeneratedClaimVerdict(x.ClaimId, verdict, reason)).ToList(),
                "synthetic", ArticleVerificationPrompt.Version));
    }

    private sealed class InvalidVerifier(string mode) : IArticleClaimVerifier
    {
        public Task<GeneratedVerificationBatch> VerifyAsync(string language, IReadOnlyList<GeneratedArticleClaim> claims,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            List<GeneratedClaimVerdict> values = mode switch
            {
                "missing" => [],
                "duplicate" => [new(claims[0].ClaimId, "supported", "ok"), new(claims[0].ClaimId, "supported", "ok")],
                _ => [new("unknown", "supported", "ok")]
            };
            return Task.FromResult(new GeneratedVerificationBatch(values, "synthetic", ArticleVerificationPrompt.Version));
        }
    }

    private sealed class OverflowGenerator : IArticleSummaryGenerator
    {
        private int calls;
        public List<ArticleSourceSpan> SuccessfulSpans { get; } = [];
        public Task<GeneratedArticleChunk> GenerateAsync(string language, string sourceKind,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            calls++;
            if (calls == 1 || sourceSpans.Count > 1) throw new AnalysisInputTooLargeException();
            SuccessfulSpans.AddRange(sourceSpans);
            ArticleSourceSpan span = sourceSpans.First(x => !string.IsNullOrWhiteSpace(x.Text));
            return Task.FromResult(new GeneratedArticleChunk(
                new([new("c" + calls, "claim " + calls, [span.SourceId])], [], [], [], []),
                "synthetic", ArticleSummaryPrompt.Version));
        }
    }
}
