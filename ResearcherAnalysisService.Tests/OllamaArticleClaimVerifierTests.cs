using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Ollama;

namespace ResearcherAnalysisService.Tests;

public sealed class OllamaArticleClaimVerifierTests
{
    [Fact]
    public async Task Verify_CitedAndNeighborContext_SendsFlagsEnumAndAcceptsMixedVerdicts()
    {
        IReadOnlyList<ArticleSourceSpan> spans =
        [
            new("s0", 1, 0, 8, "Context."),
            new("s1", 1, 8, 24, "Cited evidence."),
            new("s2", 1, 24, 38, "Qualification.")
        ];
        IReadOnlyList<GeneratedArticleClaim> claims =
        [
            new("c1", "Supported claim.", ["s1"]),
            new("c2", "Wrong quantity.", ["s1"])
        ];
        using HttpClient client = new(new StubHandler(async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("think").GetBoolean());
            Assert.Equal(8192, body.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
            string input = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            Assert.Contains("\"citedEvidence\":true", input);
            Assert.Contains("\"citedEvidence\":false", input);
            string format = body.RootElement.GetProperty("format").GetRawText();
            Assert.Contains("\"enum\":[\"c1\",\"c2\"]", format);
            return Response([new("c1", "supported", "Direct support."), new("c2", "unsupported", "Number conflicts.")]);
        }));

        GeneratedVerificationBatch result = await Verifier(client).VerifyAsync("en", claims, spans, default);

        Assert.Equal(["supported", "unsupported"], result.Verdicts.Select(x => x.Verdict));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("null")]
    public async Task Verify_InvalidVerdictSet_FailsClosed(string mode)
    {
        GeneratedArticleClaim claim = new("c1", "Claim.", ["s1"]);
        object? verdicts = mode switch
        {
            "missing" => Array.Empty<GeneratedClaimVerdict>(),
            "duplicate" => new[] { new GeneratedClaimVerdict("c1", "supported", "ok"), new("c1", "supported", "ok") },
            "unknown" => new[] { new GeneratedClaimVerdict("other", "supported", "ok") },
            _ => new object?[] { null }
        };
        using HttpClient client = new(new StubHandler(_ => Task.FromResult(ResponseObject(verdicts))));

        await Assert.ThrowsAsync<InvalidAnalysisException>(() => Verifier(client).VerifyAsync("en", [claim],
            [new("s1", 1, 0, 7, "Source.")], default));
    }

    [Fact]
    public async Task Verify_ContextAccountingInvadesReservation_RequestsFallback()
    {
        using HttpClient client = new(new StubHandler(_ => Task.FromResult(Response(
            [new("c1", "supported", "ok")], promptTokens: 29000))));
        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() => Verifier(client).VerifyAsync("en",
            [new("c1", "Claim.", ["s1"])], [new("s1", 1, 0, 7, "Source.")], default));
    }

    [Fact]
    public async Task Verify_LengthStop_RejectsIncomplete()
    {
        using HttpClient client = new(new StubHandler(_ => Task.FromResult(Response(
            [new("c1", "supported", "ok")], doneReason: "length"))));
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() => Verifier(client).VerifyAsync("en",
            [new("c1", "Claim.", ["s1"])], [new("s1", 1, 0, 7, "Source.")], default));
        Assert.Equal(AnalysisFailure.IncompleteOutput, exception.Reason);
    }

    private static OllamaArticleClaimVerifier Verifier(HttpClient client) => new(client, Options.Create(new AiOptions
        { ArticleProvider = "Ollama", ArticleModel = "qwen3.5:9b", ArticleContextTokens = 32768 }));
    private static HttpResponseMessage Response(IReadOnlyList<GeneratedClaimVerdict> verdicts,
        int promptTokens = 100, string doneReason = "stop") => ResponseObject(verdicts, promptTokens, doneReason);
    private static HttpResponseMessage ResponseObject(object? verdicts, int promptTokens = 100, string doneReason = "stop")
    {
        string content = JsonSerializer.Serialize(new { verdicts }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { model = "qwen3.5:9b", done = true, done_reason = doneReason,
                prompt_eval_count = promptTokens, eval_count = 30, message = new { content } })
        };
    }
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
