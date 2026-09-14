using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class ArticleReviewStageTests
{
    [Fact]
    public async Task GenerationRecovery_QuoteAndDispatchBindAttemptAndUseMediumBody()
    {
        RecordingGenerationHandler handler = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(geminiHandler: handler);
        ReviewArticleRequest source = Source();
        ArticleReviewRuntimeConfiguration configuration = (await host.Client.GetFromJsonAsync<
            ArticleReviewRuntimeConfiguration>("/api/v1/articles/review/configuration"))!;
        Assert.Equal(ArticleReviewGenerationRecovery.PolicyVersion,
            configuration.GenerationRecoveryPolicyVersion);
        Assert.Equal("high", configuration.GenerationThinkingLevel);
        Assert.Equal("medium", configuration.GenerationRecoveryThinkingLevel);
        Assert.Equal(8192, configuration.GenerationMaxOutputTokens);
        Assert.DoesNotContain("generationThinkingLevel", JsonSerializer.Serialize(
            new ArticleReviewStageQuoteRequest(ArticleReviewStageKinds.Generation, "method", source)));
        Guid initialAttemptId = Guid.NewGuid();
        ArticleReviewStageQuoteRequest quoteRequest = new(ArticleReviewStageKinds.Generation, "method", source)
        {
            GenerationThinkingLevel = ArticleReviewGenerationRecovery.RecoveryThinkingLevel,
            RecoveryOfAttemptId = initialAttemptId
        };
        ArticleReviewStageQuote quote = await QuoteAsync(host, quoteRequest);
        ArticleReviewStageQuote otherQuote = await QuoteAsync(host, quoteRequest with
        {
            RecoveryOfAttemptId = Guid.NewGuid()
        });
        Assert.NotEqual(quote.RequestFingerprint, otherQuote.RequestFingerprint);
        ArticleReviewStageDispatchRequest stale = new(Guid.NewGuid(), quote.SettingsFingerprint,
            quote.RequestFingerprint, quote.MaximumChargeUsd, "method", source)
        {
            GenerationThinkingLevel = ArticleReviewGenerationRecovery.RecoveryThinkingLevel,
            RecoveryOfAttemptId = Guid.NewGuid()
        };
        using HttpResponseMessage staleResponse = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/review/stages/generate", stale);
        Assert.Equal(HttpStatusCode.BadRequest, staleResponse.StatusCode);

        ArticleReviewStageDispatchRequest dispatch = stale with
        {
            RecoveryOfAttemptId = initialAttemptId
        };
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/review/stages/generate", dispatch);
        response.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        JsonElement generation = body.RootElement.GetProperty("generationConfig");
        Assert.Equal(8192, generation.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal("medium", generation.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    [Fact]
    public async Task GenerationStage_OutputLimitUsageSaveFailure_ExposesUnknownAttempt()
    {
        TestGeminiUsageRepository usage = new() { FailComplete = true };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            geminiHandler: new MaxTokensHandler(), usageRepository: usage);
        ReviewArticleRequest source = Source();
        ArticleReviewStageQuoteRequest quoteRequest = new(ArticleReviewStageKinds.Generation, "method", source);
        ArticleReviewStageQuote quote = await QuoteAsync(host, quoteRequest);
        Guid attemptId = Guid.NewGuid();
        ArticleReviewStageDispatchRequest dispatch = new(attemptId, quote.SettingsFingerprint,
            quote.RequestFingerprint, quote.MaximumChargeUsd, "method", source);

        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/review/stages/generate", dispatch);
        AnalysisErrorResponse error = (await response.Content.ReadFromJsonAsync<AnalysisErrorResponse>())!;

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("output_limit", error.ErrorCode);
        Assert.Equal(attemptId, error.ProviderAttempt!.AttemptId);
        Assert.Equal("Unknown", error.ProviderAttempt.Outcome);
        Assert.Null(error.ProviderAttempt.EstimatedCostUsd);
        Assert.Null(error.ProviderAttempt.ReturnedModel);
        Assert.Single(usage.Entries);
        Assert.Null(usage.Entries[0].Completion);
    }

    [Fact]
    public async Task VerifyStage_MaxTokens_PreservesAttemptAndKnownCostInError()
    {
        TestGeminiUsageRepository usage = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            geminiHandler: new MaxTokensHandler(), usageRepository: usage);
        ReviewArticleRequest source = Source();
        ArticleReviewCandidateFinding finding = new("F1", "teaching", "teaching_adaptation",
            "The source reports a sample.", "Use the sample as an exercise.",
            [source.SourceSpans![0].SourceId]);
        ArticleReviewStageQuoteRequest quoteRequest = new(ArticleReviewStageKinds.Verification,
            "teaching", source) { Findings = [finding] };
        using HttpResponseMessage quoteResponse = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/review/stages/quote", quoteRequest);
        quoteResponse.EnsureSuccessStatusCode();
        ArticleReviewStageQuote quote = (await quoteResponse.Content.ReadFromJsonAsync<ArticleReviewStageQuote>())!;
        Guid attemptId = Guid.NewGuid();
        ArticleReviewStageDispatchRequest dispatch = new(attemptId, quote.SettingsFingerprint,
            quote.RequestFingerprint, quote.MaximumChargeUsd, "teaching", source) { Findings = [finding] };

        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/review/stages/verify", dispatch);
        AnalysisErrorResponse error = (await response.Content.ReadFromJsonAsync<AnalysisErrorResponse>())!;

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("output_limit", error.ErrorCode);
        Assert.Equal(attemptId, error.ProviderAttempt!.AttemptId);
        Assert.Equal("OutputLimit", error.ProviderAttempt.Outcome);
        Assert.True(error.ProviderAttempt.EstimatedCostUsd > 0);
        Assert.Equal("gemini-3.8-flash-standard-through-2026-12-31",
            error.ProviderAttempt.PricingVersion);
        Assert.Single(usage.Entries);
        Assert.Equal(attemptId, usage.Entries[0].AttemptId);
        Assert.Equal("OutputLimit", usage.Entries[0].Completion!.Outcome);
    }

    private static ReviewArticleRequest Source()
    {
        IReadOnlyList<ArticlePage> pages = [new(1, "The study reports a sample of 40 participants.")];
        return new("en", "pdf", new string('a', 64), "pdf-v1",
            "article-specialist-review-policy-v1", pages, 1, false, null)
        { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    private static async Task<ArticleReviewStageQuote> QuoteAsync(AnalysisTestHost host,
        ArticleReviewStageQuoteRequest request)
    {
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/review/stages/quote", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ArticleReviewStageQuote>())!;
    }

    private sealed class RecordingGenerationHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"role\":\"method\",\"findings\":[]}"}]}}],
                      "usageMetadata":{"promptTokenCount":500,"candidatesTokenCount":20,
                        "thoughtsTokenCount":40,"totalTokenCount":560},
                      "modelVersion":"gemini-3.8-flash"
                    }
                    """, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class MaxTokensHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "candidates":[{"finishReason":"MAX_TOKENS","content":{"parts":[{"text":"{}"}]}}],
                  "usageMetadata":{"promptTokenCount":500,"candidatesTokenCount":100,
                    "thoughtsTokenCount":7000,"totalTokenCount":7600},
                  "modelVersion":"gemini-3.8-flash"
                }
                """, Encoding.UTF8, "application/json")
        });
    }
}
