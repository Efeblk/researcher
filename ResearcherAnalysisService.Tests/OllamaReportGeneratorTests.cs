using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Ollama;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class OllamaReportGeneratorTests
{
    [Fact]
    public async Task Generate_LocalModel_UsesSchemaWithoutApiKeyOrThinking()
    {
        string findings = JsonSerializer.Serialize(AnalysisSamples.Findings().Findings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using StubHandler handler = new(async request =>
        {
            Assert.Equal("http://localhost:11434/api/chat", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.False(body.RootElement.GetProperty("think").GetBoolean());
            Assert.Equal("object", body.RootElement.GetProperty("format").GetProperty("type").GetString());
            Assert.Equal(8192, body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
            Assert.Equal(2000, body.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
            Assert.DoesNotContain("Synthetic Researcher", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    model = "qwen3:1.7b", done = true, done_reason = "stop",
                    message = new { role = "assistant", content = findings }
                })
            };
        });
        using HttpClient client = new(handler);
        GeneratedFindings result = await Generator(client).GenerateAsync(AnalysisSamples.Request(), CancellationToken.None);
        Assert.Equal("qwen3:1.7b", result.Model);
        Assert.Single(result.Findings.ResearchFocus);
    }

    [Fact]
    public async Task Generate_EachSnapshot_ConstrainsEvidenceAndDoesNotReusePublicationIds()
    {
        int calls = 0;
        using StubHandler handler = new(async request =>
        {
            calls++;
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            JsonElement schema = body.RootElement.GetProperty("format");
            JsonElement definitions = schema.GetProperty("$defs");
            JsonElement evidence = definitions.GetProperty("evidence").GetProperty("properties");
            string[] ids = evidence.GetProperty("publicationId").GetProperty("enum").EnumerateArray()
                .Select(id => id.GetString()!).ToArray();
            Assert.Equal(calls == 1 ? new[] { "p1", "p2", "p3" } : new[] { "other", "p2", "p3" }, ids);
            Assert.Equal(10, evidence.GetProperty("quote").GetProperty("minLength").GetInt32());
            Assert.Equal(600, evidence.GetProperty("quote").GetProperty("maxLength").GetInt32());
            JsonElement observation = definitions.GetProperty("observation").GetProperty("properties");
            Assert.Equal(1, observation.GetProperty("evidence").GetProperty("minItems").GetInt32());
            Assert.Equal(5, observation.GetProperty("evidence").GetProperty("maxItems").GetInt32());
            Assert.Equal(2000, observation.GetProperty("observation").GetProperty("maxLength").GetInt32());
            JsonElement writing = schema.GetProperty("properties").GetProperty("writingObservations");
            Assert.Equal(calls == 1 ? 6 : 0, writing.GetProperty("maxItems").GetInt32());
            Assert.Equal("#/$defs/writingObservation", writing.GetProperty("items").GetProperty("$ref").GetString());
            Assert.Equal("#/$defs/writingEvidence", definitions.GetProperty("writingObservation")
                .GetProperty("properties").GetProperty("evidence").GetProperty("items").GetProperty("$ref").GetString());
            string?[] fields = definitions.GetProperty("writingEvidence").GetProperty("properties")
                .GetProperty("field").GetProperty("enum").EnumerateArray().Select(field => field.GetString()).ToArray();
            Assert.Equal(new[] { "abstract" }, fields);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    model = "qwen3:1.7b", done = true, done_reason = "stop",
                    message = new { content = "{\"researchFocus\":[],\"writingObservations\":[]}" }
                })
            };
        });
        using HttpClient client = new(handler);
        OllamaReportGenerator generator = Generator(client);
        await generator.GenerateAsync(AnalysisSamples.Request(), CancellationToken.None);
        var second = AnalysisSamples.Request();
        second.Publications[0].Id = "other";
        second.Publications.ForEach(publication => publication.Abstract = null);
        await generator.GenerateAsync(second, CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("{\"done\":false,\"done_reason\":\"stop\"}", AnalysisFailure.IncompleteOutput)]
    [InlineData("{\"done\":true,\"done_reason\":\"length\"}", AnalysisFailure.OutputLimit)]
    [InlineData("{\"done\":true,\"done_reason\":\"stop\",\"message\":{\"content\":\"{}\"}}", AnalysisFailure.InvalidJson)]
    [InlineData("not json", AnalysisFailure.InvalidJson)]
    public async Task Generate_TruncatedOrMalformedOutput_RejectsReport(string response, AnalysisFailure reason)
    {
        using StubHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(response) }));
        using HttpClient client = new(handler);
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() => Generator(client).GenerateAsync(AnalysisSamples.Request(), CancellationToken.None));
        Assert.Equal(reason, exception.Reason);
    }

    [Fact]
    public async Task Generate_InputExceedsContext_RejectsBeforeCallingModel()
    {
        using StubHandler handler = new(_ => throw new InvalidOperationException("Must not call model."));
        using HttpClient client = new(handler);
        var request = AnalysisSamples.Request();
        request.Publications[0].Abstract = new string('a', 10000);
        await Assert.ThrowsAsync<AnalysisInputTooLargeException>(() => Generator(client).GenerateAsync(request, CancellationToken.None));
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("file:///tmp/ollama")]
    public async Task Generate_NonLocalServer_RejectsConfiguration(string url)
    {
        using StubHandler handler = new(_ => throw new InvalidOperationException("Must not call server."));
        using HttpClient client = new(handler);
        OllamaReportGenerator generator = new(client, Options.Create(new AiOptions
        { Model = "qwen3:1.7b", OllamaBaseUrl = url }));
        await Assert.ThrowsAsync<AnalysisUnavailableException>(() => generator.GenerateAsync(AnalysisSamples.Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Generate_MissingModel_ReturnsSetupErrorWithoutDownloadOrFallback()
    {
        int calls = 0;
        using StubHandler handler = new(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        using HttpClient client = new(handler);
        AnalysisUnavailableException exception = await Assert.ThrowsAsync<AnalysisUnavailableException>(() =>
            Generator(client).GenerateAsync(AnalysisSamples.Request(), CancellationToken.None));
        Assert.Contains("ollama pull", exception.Message);
        Assert.Equal(1, calls);
    }

    private static OllamaReportGenerator Generator(HttpClient client) => new(client, Options.Create(new AiOptions
    { Model = "qwen3:1.7b" }));

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
