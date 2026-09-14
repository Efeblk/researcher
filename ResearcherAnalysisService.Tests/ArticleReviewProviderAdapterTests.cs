using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Gemini;
using ResearcherAnalysisService.Integrations.Ollama;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class ArticleReviewProviderAdapterTests
{
    [Fact]
    public async Task GeminiGenerate_RolesUseDistinctPromptsFullInputAndSchemaWithoutSourceEnum()
    {
        IReadOnlyList<ArticleSourceSpan> spans = Enumerable.Range(1, 153)
            .Select(index => new ArticleSourceSpan($"src-{index}", index, 0, 7, "Source."))
            .ToList();
        TestGeminiUsageRepository usage = new();
        List<string> instructions = [];
        using StubHandler handler = new(async (request, _) =>
        {
            Assert.Equal("synthetic-key", request.Headers.GetValues("x-goog-api-key").Single());
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("high", body.RootElement.GetProperty("generationConfig")
                .GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
            instructions.Add(body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0]
                .GetProperty("text").GetString()!);
            JsonElement schemaSourceId = body.RootElement.GetProperty("generationConfig")
                .GetProperty("responseJsonSchema").GetProperty("properties").GetProperty("findings")
                .GetProperty("items").GetProperty("properties").GetProperty("sourceIds").GetProperty("items");
            Assert.False(schemaSourceId.TryGetProperty("enum", out JsonElement ignoredEnum));
            string input = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("text").GetString()!;
            using JsonDocument inputDocument = JsonDocument.Parse(input);
            JsonElement inputRoot = inputDocument.RootElement;
            Assert.Equal(153, inputRoot.GetProperty("sources").GetArrayLength());
            Assert.Equal("src-153", inputRoot.GetProperty("sources")[152].GetProperty("sourceId").GetString());
            string role = inputRoot.GetProperty("role").GetString()!;
            return GeminiResponse(ReviewJson(role, "src-153"));
        });
        GeminiArticleReviewGenerator generator = GeminiGenerator(handler, usage);

        GeneratedArticleReviewPass method = await generator.GenerateAsync("method", "en", "pdf", spans, default);
        GeneratedArticleReviewPass quantitative = await generator.GenerateAsync(
            "quantitative", "en", "pdf", spans, default);

        Assert.Equal("src-153", method.Findings.Single().SourceIds.Single());
        Assert.Equal("src-153", quantitative.Findings.Single().SourceIds.Single());
        Assert.Contains("reported design, sampling, controls", instructions[0]);
        Assert.Contains("reported numbers, units, denominators", instructions[1]);
        Assert.Contains("O(sqrt(T))", instructions[0]);
        Assert.Contains("does not relax this requirement", instructions[0]);
        Assert.Contains("condition-only statement", instructions[0]);
        Assert.NotEqual(instructions[0], instructions[1]);
        Assert.Equal(2, usage.Entries.Count);
        Assert.All(usage.Entries, entry => Assert.Equal("Success", entry.Completion!.Outcome));
    }

    [Fact]
    public async Task GeminiVerify_FormalGuaranteeProtocolKeepsOwnCitationsAndNarrowerStatements()
    {
        const string leadText =
            "The authors give an O(sqrt(T)) regret bound for online convex functions.";
        const string conditionText =
            "The theorem assumes bounded gradients and bounded iterate distances, with gamma_t = 1/t.";
        IReadOnlyList<ArticleSourceSpan> spans =
        [
            new("lead", 1, 0, leadText.Length, leadText),
            new("conditions", 2, 0, conditionText.Length, conditionText)
        ];
        IReadOnlyList<GeneratedArticleReviewFinding> findings =
        [
            new("broad", "claim_evidence", "source_observation",
                "The authors proved an O(sqrt(T)) regret bound.", null, ["lead"]),
            new("qualified", "claim_evidence", "source_observation",
                "The authors proved an O(sqrt(T)) regret bound for online convex functions under bounded " +
                "gradients, bounded iterate distances, and gamma_t = 1/t.", null, ["lead", "conditions"]),
            new("necessary", "claim_evidence", "source_observation",
                "The regret theorem assumes bounded gradients.", null, ["conditions"])
        ];
        string? instructions = null;
        using StubHandler handler = new(async (request, _) =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            instructions = body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0]
                .GetProperty("text").GetString();
            string input = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("text").GetString()!;
            using JsonDocument inputDocument = JsonDocument.Parse(input);
            JsonElement[] items = inputDocument.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(["lead"], SourceIds(items[0]));
            Assert.Equal(["lead", "conditions"], SourceIds(items[1]));
            Assert.Equal(["conditions"], SourceIds(items[2]));
            return GeminiResponse("""
                {"verdicts":[
                  {"findingId":"broad","verdict":"uncertain","reason":"The cited lead-in omits material theorem conditions."},
                  {"findingId":"qualified","verdict":"supported","reason":"The result and material conditions are stated and cited."},
                  {"findingId":"necessary","verdict":"supported","reason":"This reports one cited assumption without claiming sufficiency."}
                ]}
                """);
        });
        TestGeminiUsageRepository usage = new();

        GeneratedArticleReviewVerification result = await GeminiVerifier(handler, usage)
            .VerifyAsync("claim_evidence", "en", findings, spans, default);

        Assert.Equal(["uncertain", "supported", "supported"],
            result.Verdicts.Select(value => value.Verdict).ToArray());
        Assert.Contains("grants no exemption", instructions);
        Assert.Contains("condition-only observation", instructions);
        Assert.Contains("neutral meta-observation", instructions);
        Assert.Equal(ArticleReviewVerificationPrompt.Version, result.PromptVersion);
        Assert.Single(usage.Entries);
    }

    [Fact]
    public async Task GeminiVerify_UsesVerifierModelPromptCiteOnlyItemAndUsageLedger()
    {
        IReadOnlyList<ArticleSourceSpan> spans =
        [
            new("s0", 1, 0, 8, "Context."),
            new("s1", 1, 8, 24, "Cited evidence."),
            new("s2", 1, 24, 38, "Qualification.")
        ];
        GeneratedArticleReviewFinding finding = new(
            "f1", "method", "source_observation", "Supported basis.", null, ["s1"]);
        TestGeminiUsageRepository usage = new();
        using StubHandler handler = new(async (request, _) =>
        {
            Assert.Contains("models/gemini-review-verifier:generateContent", request.RequestUri!.AbsoluteUri);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("low", body.RootElement.GetProperty("generationConfig")
                .GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
            Assert.Equal(ArticleReviewVerificationPrompt.Instructions,
                body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0]
                    .GetProperty("text").GetString());
            string input = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("text").GetString()!;
            using JsonDocument inputDocument = JsonDocument.Parse(input);
            JsonElement item = inputDocument.RootElement.GetProperty("items")[0];
            Assert.Equal("f1", item.GetProperty("finding").GetProperty("findingId").GetString());
            Assert.Equal("s1", Assert.Single(item.GetProperty("sources").EnumerateArray())
                .GetProperty("sourceId").GetString());
            Assert.DoesNotContain("Context.", input);
            Assert.DoesNotContain("Qualification.", input);
            Assert.DoesNotContain("citedEvidence", input);
            JsonElement findingId = body.RootElement.GetProperty("generationConfig")
                .GetProperty("responseJsonSchema").GetProperty("properties").GetProperty("verdicts")
                .GetProperty("items").GetProperty("properties").GetProperty("findingId");
            Assert.Equal("f1", findingId.GetProperty("enum")[0].GetString());
            return GeminiResponse(VerificationJson(), modelVersion: "gemini-review-verifier");
        });
        AiOptions settings = new()
        {
            ArticleVerifierModel = "gemini-review-verifier",
            ArticleVerifierThinkingLevel = "low"
        };

        GeneratedArticleReviewVerification result = await GeminiVerifier(handler, usage, settings)
            .VerifyAsync("method", "en", [finding], spans, default);

        Assert.Equal("supported", result.Verdicts.Single().Verdict);
        Assert.Equal("gemini-review-verifier", result.Model);
        TestGeminiUsageRepository.Entry attempt = Assert.Single(usage.Entries);
        Assert.Equal("gemini-review-verifier", attempt.RequestedModel);
        Assert.Equal("Success", attempt.Completion!.Outcome);
    }

    [Fact]
    public async Task GeminiAdapters_UnknownJsonMembersFailClosed()
    {
        Queue<HttpResponseMessage> responses = new([
            GeminiResponse("{\"role\":\"method\",\"findings\":[],\"unexpected\":true}"),
            GeminiResponse("{\"verdicts\":[{\"findingId\":\"f1\",\"verdict\":\"supported\",\"reason\":\"ok\",\"unexpected\":true}]}")
        ]);
        using StubHandler handler = new((_, _) => Task.FromResult(responses.Dequeue()));
        TestGeminiUsageRepository usage = new();
        GeneratedArticleReviewFinding finding = new(
            "f1", "method", "source_observation", "Basis.", null, ["s1"]);

        InvalidAnalysisException generation = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            GeminiGenerator(handler, usage).GenerateAsync("method", "en", "pdf", [Span()], default));
        InvalidAnalysisException verification = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            GeminiVerifier(handler, usage).VerifyAsync("method", "en", [finding], [Span()], default));

        Assert.Equal(AnalysisFailure.InvalidJson, generation.Reason);
        Assert.Equal(AnalysisFailure.InvalidJson, verification.Reason);
        Assert.Equal(2, usage.Entries.Count);
    }

    [Theory]
    [InlineData("MAX_TOKENS", AnalysisFailure.OutputLimit)]
    [InlineData("SAFETY", AnalysisFailure.InvalidJson)]
    public async Task GeminiGenerate_NonStopFinishFailsClosed(string finishReason, AnalysisFailure expected)
    {
        using StubHandler handler = new((_, _) => Task.FromResult(GeminiResponse("{}", finishReason)));
        TestGeminiUsageRepository usage = new();

        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            GeminiGenerator(handler, usage).GenerateAsync("method", "en", "pdf", [Span()], default));

        Assert.Equal(expected, exception.Reason);
        Assert.Equal(finishReason == "MAX_TOKENS" ? "OutputLimit" : "InvalidJson",
            Assert.Single(usage.Entries).Completion!.Outcome);
    }

    [Fact]
    public async Task OllamaGenerate_UsesCapabilityProbeRolePromptFullInputAndUntruncatedRequest()
    {
        IReadOnlyList<ArticleSourceSpan> spans =
        [
            new("s0", 1, 0, 8, "Context."),
            new("s1", 1, 8, 24, "Cited evidence."),
            new("s2", 1, 24, 38, "Qualification.")
        ];
        int versionCalls = 0;
        int chatCalls = 0;
        using OllamaStubHandler handler = new(async request =>
        {
            chatCalls++;
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("truncate").GetBoolean());
            Assert.False(body.RootElement.GetProperty("shift").GetBoolean());
            Assert.False(body.RootElement.GetProperty("think").GetBoolean());
            Assert.Equal(32768, body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
            Assert.Contains("reported design, sampling, controls",
                body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
            string input = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using JsonDocument inputDocument = JsonDocument.Parse(input);
            Assert.Equal(3, inputDocument.RootElement.GetProperty("sources").GetArrayLength());
            Assert.Equal("s2", inputDocument.RootElement.GetProperty("sources")[2]
                .GetProperty("sourceId").GetString());
            string schema = body.RootElement.GetProperty("format").GetRawText();
            Assert.Contains("\"enum\":[\"s0\",\"s1\",\"s2\"]", schema);
            return OllamaResponse(ReviewJson("method", "s1"));
        }, onVersion: () => versionCalls++);

        GeneratedArticleReviewPass result = await OllamaGenerator(new HttpClient(handler))
            .GenerateAsync("method", "en", "pdf", spans, default);

        Assert.Single(result.Findings);
        Assert.Equal(1, versionCalls);
        Assert.Equal(1, chatCalls);
    }

    [Theory]
    [InlineData("https://example.com/", "qwen3.5:9b")]
    [InlineData("http://localhost:11434/", "qwen3.5:cloud")]
    [InlineData("http://localhost:11434/", "review-cloud")]
    public async Task OllamaGenerate_NonLocalConfigurationFailsBeforeHttp(string baseUrl, string model)
    {
        int sends = 0;
        using OllamaStubHandler handler = new(_ =>
        {
            sends++;
            return Task.FromResult(OllamaResponse(ReviewJson("method", "src-1")));
        });
        AiOptions settings = OllamaSettings();
        settings.OllamaBaseUrl = baseUrl;
        settings.ArticleModel = model;

        await Assert.ThrowsAsync<AnalysisUnavailableException>(() =>
            OllamaGenerator(new HttpClient(handler), settings)
                .GenerateAsync("method", "en", "pdf", [Span()], default));

        Assert.Equal(0, sends);
    }

    [Fact]
    public async Task OllamaGenerate_UnsupportedVersionFailsBeforeChat()
    {
        int chatCalls = 0;
        using OllamaStubHandler handler = new(request =>
        {
            chatCalls++;
            return Task.FromResult(OllamaResponse(ReviewJson("method", "src-1")));
        }, version: "0.32.0");

        await Assert.ThrowsAsync<AnalysisUnavailableException>(() =>
            OllamaGenerator(new HttpClient(handler)).GenerateAsync(
                "method", "en", "pdf", [Span()], default));

        Assert.Equal(0, chatCalls);
    }

    [Fact]
    public async Task OllamaVerify_CloudOverrideFailsBeforeChat()
    {
        int chatCalls = 0;
        using OllamaStubHandler handler = new(_ =>
        {
            chatCalls++;
            return Task.FromResult(OllamaResponse(VerificationJson()));
        });
        AiOptions settings = OllamaSettings();
        settings.ArticleVerifierModel = "review-cloud";
        GeneratedArticleReviewFinding finding = new(
            "f1", "method", "source_observation", "Basis.", null, ["src-1"]);

        await Assert.ThrowsAsync<AnalysisUnavailableException>(() =>
            OllamaVerifier(new HttpClient(handler), settings)
                .VerifyAsync("method", "en", [finding], [Span()], default));

        Assert.Equal(0, chatCalls);
    }

    [Fact]
    public async Task OllamaGenerate_CompleteSerializedRequestOverflowFailsBeforeChat()
    {
        IReadOnlyList<ArticleSourceSpan> spans = Enumerable.Range(1, 18)
            .Select(index => new ArticleSourceSpan($"source-{index}-{new string('x', 70)}", 1, 0, 1, "S"))
            .ToList();
        int chatCalls = 0;
        using OllamaStubHandler handler = new(request =>
        {
            chatCalls++;
            return Task.FromResult(OllamaResponse("{\"role\":\"method\",\"findings\":[]}"));
        });
        AiOptions settings = OllamaSettings();
        settings.ArticleContextTokens = 4096;
        settings.ArticleMaxOutputTokens = 512;

        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() =>
            OllamaGenerator(new HttpClient(handler), settings)
                .GenerateAsync("method", "en", "pdf", spans, default));

        Assert.Equal(0, chatCalls);
    }

    [Fact]
    public async Task OllamaVerify_UsesCapabilityProbeVerifierModelAndCiteOnlyItem()
    {
        IReadOnlyList<ArticleSourceSpan> spans =
        [
            new("s0", 1, 0, 8, "Context."),
            new("s1", 1, 8, 24, "Cited evidence."),
            new("s2", 1, 24, 38, "Qualification.")
        ];
        GeneratedArticleReviewFinding finding = new(
            "f1", "method", "source_observation", "Basis.", null, ["s1"]);
        int versionCalls = 0;
        using OllamaStubHandler handler = new(async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("qwen3.5:14b", body.RootElement.GetProperty("model").GetString());
            Assert.True(body.RootElement.GetProperty("think").GetBoolean());
            Assert.False(body.RootElement.GetProperty("truncate").GetBoolean());
            Assert.False(body.RootElement.GetProperty("shift").GetBoolean());
            Assert.Equal(2048, body.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
            string input = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using JsonDocument inputDocument = JsonDocument.Parse(input);
            JsonElement item = inputDocument.RootElement.GetProperty("items")[0];
            Assert.Equal("f1", item.GetProperty("finding").GetProperty("findingId").GetString());
            Assert.Equal("s1", Assert.Single(item.GetProperty("sources").EnumerateArray())
                .GetProperty("sourceId").GetString());
            Assert.DoesNotContain("Context.", input);
            Assert.DoesNotContain("Qualification.", input);
            Assert.DoesNotContain("citedEvidence", input);
            return OllamaResponse(VerificationJson(), model: "qwen3.5:14b");
        }, onVersion: () => versionCalls++);
        AiOptions settings = OllamaSettings();
        settings.ArticleVerifierModel = "qwen3.5:14b";
        settings.ArticleVerifierMaxOutputTokens = 2048;

        GeneratedArticleReviewVerification result = await OllamaVerifier(new HttpClient(handler), settings)
            .VerifyAsync("method", "en", [finding], spans, default);

        Assert.Equal("qwen3.5:14b", result.Model);
        Assert.Equal("supported", result.Verdicts.Single().Verdict);
        Assert.Equal(1, versionCalls);
    }

    [Fact]
    public async Task OllamaAdapters_UnknownJsonMembersFailClosed()
    {
        Queue<HttpResponseMessage> responses = new([
            OllamaResponse("{\"role\":\"method\",\"findings\":[],\"unexpected\":true}"),
            OllamaResponse("{\"verdicts\":[{\"findingId\":\"f1\",\"verdict\":\"supported\",\"reason\":\"ok\",\"unexpected\":true}]}" )
        ]);
        using OllamaStubHandler handler = new(_ => Task.FromResult(responses.Dequeue()));
        GeneratedArticleReviewFinding finding = new(
            "f1", "method", "source_observation", "Basis.", null, ["s1"]);

        InvalidAnalysisException generation = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            OllamaGenerator(new HttpClient(handler)).GenerateAsync("method", "en", "pdf", [Span()], default));
        InvalidAnalysisException verification = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            OllamaVerifier(new HttpClient(handler)).VerifyAsync("method", "en", [finding], [Span()], default));

        Assert.Equal(AnalysisFailure.InvalidJson, generation.Reason);
        Assert.Equal(AnalysisFailure.InvalidJson, verification.Reason);
    }

    [Fact]
    public async Task OllamaGenerate_LengthStopAndTokenOverflowFailClosed()
    {
        using OllamaStubHandler lengthHandler = new(_ => Task.FromResult(OllamaResponse(
            ReviewJson("method", "src-1"), doneReason: "length")));
        InvalidAnalysisException incomplete = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            OllamaGenerator(new HttpClient(lengthHandler)).GenerateAsync(
                "method", "en", "pdf", [Span()], default));

        using OllamaStubHandler overflowHandler = new(_ => Task.FromResult(OllamaResponse(
            ReviewJson("method", "src-1"), promptTokens: 29000)));
        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() =>
            OllamaGenerator(new HttpClient(overflowHandler)).GenerateAsync(
                "method", "en", "pdf", [Span()], default));

        Assert.Equal(AnalysisFailure.IncompleteOutput, incomplete.Reason);
    }

    [Fact]
    public async Task OllamaGenerate_ProviderRefusalAndContextErrorFailClosed()
    {
        Queue<HttpResponseMessage> responses = new([
            new HttpResponseMessage(HttpStatusCode.NotFound),
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { error = "input exceeds context token limit" })
            }
        ]);
        using OllamaStubHandler handler = new(_ => Task.FromResult(responses.Dequeue()));

        await Assert.ThrowsAsync<AnalysisUnavailableException>(() =>
            OllamaGenerator(new HttpClient(handler)).GenerateAsync("method", "en", "pdf", [Span()], default));
        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() =>
            OllamaGenerator(new HttpClient(handler)).GenerateAsync("method", "en", "pdf", [Span()], default));
    }

    private static string[] SourceIds(JsonElement item) => item.GetProperty("sources").EnumerateArray()
        .Select(value => value.GetProperty("sourceId").GetString()!).ToArray();

    private static ArticleSourceSpan Span() => new("src-1", 1, 0, 7, "Source.");

    private static string ReviewJson(string role, string sourceId) => JsonSerializer.Serialize(new
    {
        role,
        findings = new[]
        {
            new
            {
                findingId = "f1",
                role,
                kind = "source_observation",
                basis = "Supported basis.",
                suggestion = (string?)null,
                sourceIds = new[] { sourceId }
            }
        }
    });

    private static string VerificationJson() => JsonSerializer.Serialize(new
    {
        verdicts = new[] { new { findingId = "f1", verdict = "supported", reason = "Direct support." } }
    });

    private static GeminiArticleReviewGenerator GeminiGenerator(HttpMessageHandler handler,
        TestGeminiUsageRepository? usage = null, AiOptions? settings = null)
    {
        IOptions<AiOptions> ai = Options.Create(settings ?? new AiOptions());
        GeminiArticleClient client = new(new HttpClient(handler), ai,
            Options.Create(new GeminiOptions { ApiKey = "synthetic-key" }), usage ?? new());
        return new(client, ai);
    }

    private static GeminiArticleReviewVerifier GeminiVerifier(HttpMessageHandler handler,
        TestGeminiUsageRepository? usage = null, AiOptions? settings = null)
    {
        IOptions<AiOptions> ai = Options.Create(settings ?? new AiOptions());
        GeminiArticleClient client = new(new HttpClient(handler), ai,
            Options.Create(new GeminiOptions { ApiKey = "synthetic-key" }), usage ?? new());
        return new(client, ai);
    }

    private static OllamaArticleReviewGenerator OllamaGenerator(HttpClient client, AiOptions? settings = null) =>
        new(client, Options.Create(settings ?? OllamaSettings()));

    private static OllamaArticleReviewVerifier OllamaVerifier(HttpClient client, AiOptions? settings = null) =>
        new(client, Options.Create(settings ?? OllamaSettings()));

    private static AiOptions OllamaSettings() => new()
    {
        ArticleProvider = "Ollama",
        ArticleModel = "qwen3.5:9b",
        OllamaBaseUrl = "http://localhost:11434/",
        ArticleContextTokens = 32768,
        ArticleMaxOutputTokens = 4000,
        ArticleVerifierMaxOutputTokens = 4000
    };

    private static HttpResponseMessage GeminiResponse(string answer, string finishReason = "STOP",
        string modelVersion = "gemini-3.8-flash") =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text = answer } } }, finishReason }
                },
                usageMetadata = new
                {
                    promptTokenCount = 100,
                    candidatesTokenCount = 20,
                    totalTokenCount = 120
                },
                modelVersion
            })
        };

    private static HttpResponseMessage OllamaResponse(string content, string model = "qwen3.5:9b",
        int promptTokens = 100, int generatedTokens = 30, string doneReason = "stop") =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                model,
                done = true,
                done_reason = doneReason,
                prompt_eval_count = promptTokens,
                eval_count = generatedTokens,
                message = new { content }
            })
        };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class OllamaStubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send,
        string version = "0.33.3",
        Action? onVersion = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/version")
            {
                onVersion?.Invoke();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { version })
                });
            }
            return send(request);
        }
    }
}
