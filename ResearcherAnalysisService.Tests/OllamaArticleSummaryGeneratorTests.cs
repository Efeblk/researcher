using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Ollama;

namespace ResearcherAnalysisService.Tests;

public sealed class OllamaArticleSummaryGeneratorTests
{
    [Fact]
    public async Task Generate_DefaultRequest_UsesSourceIdsAndUntruncatedContext()
    {
        string text = "T\u00fcrk\u00e7e ara\u015ft\u0131rma bulgusu \u00f6l\u00e7\u00fcld\u00fc.";
        using StubHandler handler = new(async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Contains("src-1", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            Assert.False(body.RootElement.GetProperty("truncate").GetBoolean());
            Assert.False(body.RootElement.GetProperty("shift").GetBoolean());
            Assert.Equal(32768, body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
            string content = JsonSerializer.Serialize(new GeneratedArticleSections(
                [new("c1", "Ara\u015ft\u0131rma bir bulgu bildirdi.", ["src-1"])], [], [], [], []),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Response(content);
        });

        GeneratedArticleChunk result = await Generator(new(handler)).GenerateAsync("tr", "pdf", [Span(text)], default);

        Assert.Single(result.Sections.Purpose);
    }

    [Fact]
    public async Task Generate_UnknownSourceId_Rejects()
    {
        string content = JsonSerializer.Serialize(new GeneratedArticleSections(
            [new("c1", "Invented", ["missing"])], [], [], [], []), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using StubHandler handler = new(_ => Task.FromResult(Response(content)));

        await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Generator(new(handler)).GenerateAsync("en", "pdf", [Span("A source passage.")], default));
    }

    [Fact]
    public async Task Generate_RepeatedValidSourceId_NormalizesToSingleReference()
    {
        string content = JsonSerializer.Serialize(new GeneratedArticleSections(
            [new("c1", "Supported", ["src-1", "src-1"])], [], [], [], []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using StubHandler handler = new(_ => Task.FromResult(Response(content)));

        GeneratedArticleChunk result = await Generator(new(handler)).GenerateAsync(
            "en", "pdf", [Span("A source passage.")], default);

        Assert.Equal(["src-1"], result.Sections.Purpose.Single().SourceIds);
    }

    [Fact]
    public async Task Generate_RepeatedUnknownSourceId_StillRejects()
    {
        string content = JsonSerializer.Serialize(new GeneratedArticleSections(
            [new("c1", "Unsupported", ["missing", "missing"])], [], [], [], []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using StubHandler handler = new(_ => Task.FromResult(Response(content)));

        await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Generator(new(handler)).GenerateAsync("en", "pdf", [Span("A source passage.")], default));
    }

    [Fact]
    public async Task Generate_PromptInvadesReservation_RequestsAtomicFallback()
    {
        string content = JsonSerializer.Serialize(new GeneratedArticleSections([], [], [], [], []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using StubHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { model = "qwen3.5:9b", done = true, done_reason = "stop",
                prompt_eval_count = 29000, eval_count = 20, message = new { content } })
        }));

        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() =>
            Generator(new(handler)).GenerateAsync("en", "pdf", [Span("A source passage.")], default));
    }

    [Fact]
    public async Task Generate_ContextLengthBadRequest_RequestsFallback()
    {
        using StubHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = JsonContent.Create(new { error = "input length exceeds context length" }) }));
        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() =>
            Generator(new(handler)).GenerateAsync("en", "pdf", [Span("A source passage.")], default));
    }

    [Fact]
    public async Task Generate_LengthStop_RejectsIncompleteOutput()
    {
        using StubHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { model = "qwen3.5:9b", done = true, done_reason = "length",
                prompt_eval_count = 100, eval_count = 2000, message = new { content = "{}" } })
        }));
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Generator(new(handler)).GenerateAsync("en", "pdf", [Span("A source passage.")], default));
        Assert.Equal(AnalysisFailure.IncompleteOutput, exception.Reason);
    }

    [Theory]
    [InlineData("0.32.0")]
    [InlineData("not-a-version")]
    public async Task Generate_UnsupportedOllamaVersion_FailsClosed(string version)
    {
        using StubHandler handler = new(_ => throw new InvalidOperationException(), version);
        await Assert.ThrowsAsync<AnalysisUnavailableException>(() =>
            Generator(new(handler)).GenerateAsync("en", "pdf", [Span("A source passage.")], default));
    }

    private static ArticleSourceSpan Span(string text) => new("src-1", 1, 0, text.Length, text);
    private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { model = "qwen3.5:9b", done = true, done_reason = "stop",
            prompt_eval_count = 100, eval_count = 30, message = new { content } })
    };
    private static OllamaArticleSummaryGenerator Generator(HttpClient client) =>
        new(client, Options.Create(new AiOptions { ArticleProvider = "Ollama", ArticleModel = "qwen3.5:9b",
            ArticleContextTokens = 32768, ArticleMaxOutputTokens = 4000 }));
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send,
        string version = "0.33.3") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath == "/api/version"
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { version }) })
                : send(request);
    }
}
