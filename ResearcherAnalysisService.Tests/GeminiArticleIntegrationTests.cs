using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Gemini;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class GeminiArticleIntegrationTests
{
    [Fact]
    public async Task Generate_ValidResponse_UsesBenchmarkRequestAndParsesStrictJson()
    {
        using StubHandler handler = new(async (request, _) =>
        {
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal("synthetic-key", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.DoesNotContain("synthetic-key", request.RequestUri.AbsoluteUri);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            JsonElement config = body.RootElement.GetProperty("generationConfig");
            Assert.Equal("high", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
            Assert.False(config.GetProperty("thinkingConfig").GetProperty("includeThoughts").GetBoolean());
            Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
            Assert.DoesNotContain("src-1", config.GetProperty("responseJsonSchema").GetRawText());
            Assert.Contains("src-1", body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("text").GetString());
            return Response("{\"purpose\":[{\"claimId\":\"c1\",\"text\":\"Supported.\",\"sourceIds\":[\"src-1\"]}],\"methods\":[],\"data\":[],\"findings\":[],\"limitations\":[]}");
        });

        GeneratedArticleChunk result = await Summary(handler).GenerateAsync("en", "pdf", [Span()], default);

        Assert.Single(result.Sections.Purpose);
        Assert.Equal("gemini-3.8-flash", result.Model);
    }

    [Fact]
    public async Task Generate_ManySources_OmitsSchemaEnumAndRejectsInventedSourceId()
    {
        IReadOnlyList<ArticleSourceSpan> spans = Enumerable.Range(1, 153)
            .Select(index => new ArticleSourceSpan($"src-{index}", index, 0, 7, "Source."))
            .ToList();
        using StubHandler handler = new(async (request, cancellationToken) =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            JsonElement schema = body.RootElement.GetProperty("generationConfig").GetProperty("responseJsonSchema");
            JsonElement sourceId = schema.GetProperty("$defs").GetProperty("claims").GetProperty("items")
                .GetProperty("properties").GetProperty("sourceIds").GetProperty("items");
            Assert.False(sourceId.TryGetProperty("enum", out JsonElement _));
            Assert.Contains("src-153", body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("text").GetString());
            return Response("{\"purpose\":[{\"claimId\":\"c1\",\"text\":\"Invented.\",\"sourceIds\":[\"src-invented\"]}],\"methods\":[],\"data\":[],\"findings\":[],\"limitations\":[]}");
        });

        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", spans, default));

        Assert.Equal(AnalysisFailure.InvalidEvidence, exception.Reason);
    }

    [Fact]
    public async Task Generate_MaxTokens_FailsAsIncomplete()
    {
        using StubHandler handler = new((_, _) => Task.FromResult(Response("{}", "MAX_TOKENS")));
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", [Span()], default));
        Assert.Equal(AnalysisFailure.IncompleteOutput, exception.Reason);
    }

    [Fact]
    public async Task Generate_SafetyFinish_FailsWithoutAdaptiveRetrySignal()
    {
        using StubHandler handler = new((_, _) => Task.FromResult(Response("{}", "SAFETY")));
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", [Span()], default));
        Assert.Equal(AnalysisFailure.InvalidJson, exception.Reason);
    }

    [Fact]
    public async Task Generate_BlockedResponseWithoutCandidate_FailsClosed()
    {
        using StubHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { promptFeedback = new { blockReason = "SAFETY" },
                usageMetadata = new { promptTokenCount = 10 } })
        }));
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", [Span()], default));
        Assert.Equal(AnalysisFailure.InvalidJson, exception.Reason);
    }

    [Fact]
    public async Task Generate_EmptyCandidates_FailsWithoutAdaptiveRetrySignal()
    {
        using StubHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { candidates = Array.Empty<object>(),
                usageMetadata = new { promptTokenCount = 10 } })
        }));
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", [Span()], default));
        Assert.Equal(AnalysisFailure.InvalidJson, exception.Reason);
    }

    [Fact]
    public async Task Generate_EmptyAnswer_FailsClosed()
    {
        using StubHandler handler = new((_, _) => Task.FromResult(Response("")));
        await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", [Span()], default));
    }

    [Fact]
    public async Task Verify_InvalidJsonAndUnknownVerdict_FailClosed()
    {
        Queue<HttpResponseMessage> responses = new([
            Response("{not-json}"),
            Response("{\"verdicts\":[{\"claimId\":\"other\",\"verdict\":\"supported\",\"reason\":\"ok\"}]}")
        ]);
        using StubHandler handler = new((_, _) => Task.FromResult(responses.Dequeue()));
        GeminiArticleClaimVerifier verifier = Verifier(handler);
        GeneratedArticleClaim claim = new("c1", "Claim.", ["src-1"]);
        await Assert.ThrowsAsync<InvalidAnalysisException>(() => verifier.VerifyAsync("en", [claim], [Span()], default));
        await Assert.ThrowsAsync<InvalidAnalysisException>(() => verifier.VerifyAsync("en", [claim], [Span()], default));
    }

    [Fact]
    public async Task Verify_ValidResponse_UsesOverrideModelAndNeighborGrounding()
    {
        using StubHandler handler = new(async (request, _) =>
        {
            Assert.Contains("models/gemini-verifier:generateContent", request.RequestUri!.AbsoluteUri);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            string input = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("text").GetString()!;
            Assert.Contains("\"citedEvidence\":true", input);
            Assert.Contains("\"citedEvidence\":false", input);
            Assert.Contains("no more than 500 characters", body.RootElement.GetProperty("systemInstruction")
                .GetProperty("parts")[0].GetProperty("text").GetString());
            return Response("{\"verdicts\":[{\"claimId\":\"c1\",\"verdict\":\"supported\",\"reason\":\"Direct support.\"}]}");
        });
        AiOptions settings = new() { ArticleVerifierModel = "gemini-verifier" };
        GeminiArticleClaimVerifier verifier = Verifier(handler, settings);
        IReadOnlyList<ArticleSourceSpan> spans =
        [
            new("src-0", 1, 0, 8, "Context."),
            new("src-1", 1, 8, 15, "Source."),
            new("src-2", 1, 15, 28, "Qualification.")
        ];

        GeneratedVerificationBatch result = await verifier.VerifyAsync("en",
            [new("c1", "Claim.", ["src-1"])], spans, default);

        Assert.Equal("supported", result.Verdicts.Single().Verdict);
    }

    [Fact]
    public async Task Generate_NullSection_FailsClosed()
    {
        using StubHandler handler = new((_, _) => Task.FromResult(Response(
            "{\"purpose\":null,\"methods\":[],\"data\":[],\"findings\":[],\"limitations\":[]}")));
        await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", [Span()], default));
    }

    [Fact]
    public async Task Generate_AuthenticationError_IsUnavailable()
    {
        using StubHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        await Assert.ThrowsAsync<AnalysisUnavailableException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", [Span()], default));
    }

    [Fact]
    public async Task Generate_Cancellation_Propagates()
    {
        using StubHandler handler = new(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response("{}");
        });
        using CancellationTokenSource source = new();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Summary(handler).GenerateAsync("en", "pdf", [Span()], source.Token));
    }

    [Fact]
    public void CreateApplication_DefaultArticleProvider_ResolvesGeminiAdapters()
    {
        using WebApplication application = Program.CreateApplication([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "synthetic-key"
            });
        });
        using IServiceScope scope = application.Services.CreateScope();
        Assert.IsType<GeminiArticleSummaryGenerator>(scope.ServiceProvider.GetRequiredService<IArticleSummaryGenerator>());
        Assert.IsType<GeminiArticleClaimVerifier>(scope.ServiceProvider.GetRequiredService<IArticleClaimVerifier>());
    }

    [Fact]
    public async Task SummarizeEndpoint_ConfiguredArticleServices_ReturnsCheckedReport()
    {
        ArticlePage page = new(1, "Synthetic grounded source.");
        ArticleSourceSpan span = ArticleSourceCatalog.Create([page]).Single();
        int calls = 0;
        using StubHandler handler = new((request, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? Response($"{{\"purpose\":[],\"methods\":[],\"data\":[],\"findings\":[{{\"claimId\":\"c1\",\"text\":\"Grounded finding.\",\"sourceIds\":[\"{span.SourceId}\"]}}],\"limitations\":[]}}")
                : Response("{\"verdicts\":[{\"claimId\":\"chunk-1:c1\",\"verdict\":\"supported\",\"reason\":\"Direct support.\"}]}"));
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(geminiHandler: handler);
        SummarizeArticleRequest request = new("en", "pdf", "synthetic-hash", "synthetic-v1", [page], 1,
            false, null) { SourceSpans = [span] };

        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/articles/summarize", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ArticleSummaryReport report = (await response.Content.ReadFromJsonAsync<ArticleSummaryReport>())!;
        Assert.Equal("automatically_checked", report.Verification!.Status);
        Assert.Single(report.Sections.Findings);
        Assert.Equal("gemini-3.8-flash", report.Model);
        Assert.Equal(2, calls);
    }

    private static ArticleSourceSpan Span() => new("src-1", 1, 0, 7, "Source.");

    private static GeminiArticleSummaryGenerator Summary(HttpMessageHandler handler)
    {
        IOptions<AiOptions> ai = Options.Create(new AiOptions());
        GeminiArticleClient client = new(new HttpClient(handler), ai,
            Options.Create(new GeminiOptions { ApiKey = "synthetic-key" }));
        return new(client, ai);
    }

    private static GeminiArticleClaimVerifier Verifier(HttpMessageHandler handler, AiOptions? settings = null)
    {
        IOptions<AiOptions> ai = Options.Create(settings ?? new AiOptions());
        GeminiArticleClient client = new(new HttpClient(handler), ai,
            Options.Create(new GeminiOptions { ApiKey = "synthetic-key" }));
        return new(client, ai);
    }

    private static HttpResponseMessage Response(string answer, string finishReason = "STOP") =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                candidates = new[] { new { content = new { parts = new[] { new { text = answer } } }, finishReason } },
                usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 20, totalTokenCount = 120 },
                modelVersion = "gemini-3.8-flash"
            })
        };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

}
