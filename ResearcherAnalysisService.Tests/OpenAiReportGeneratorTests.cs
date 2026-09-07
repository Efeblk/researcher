using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.OpenAi;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class OpenAiReportGeneratorTests
{
    [Fact]
    public async Task Generate_CompletedResponse_UsesStrictSchemaAndParsesMessageAfterReasoning()
    {
        string findings = JsonSerializer.Serialize(AnalysisSamples.Findings().Findings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using StubHandler handler = new(async request =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            Assert.Equal("json_schema", body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
            Assert.DoesNotContain("Synthetic Researcher", body.RootElement.GetProperty("input").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    status = "completed", model = "configured-model-snapshot",
                    output = new object[]
                    {
                        new { type = "reasoning" },
                        new { type = "message", content = new[] { new { type = "output_text", text = findings } } }
                    }
                })
            };
        });
        using HttpClient client = new(handler);
        GeneratedFindings result = await Generator(client).GenerateAsync(AnalysisSamples.Request(), CancellationToken.None);
        Assert.Equal("configured-model-snapshot", result.Model);
        Assert.Single(result.Findings.ResearchFocus);
    }

    [Theory]
    [InlineData("{\"status\":\"incomplete\"}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\"}]}]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{}\"}]}]}")]
    [InlineData("not json")]
    public async Task Generate_IncompleteRefusedOrMalformedResponse_RejectsReport(string response)
    {
        using StubHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(response) }));
        using HttpClient client = new(handler);
        await Assert.ThrowsAsync<InvalidAnalysisException>(() => Generator(client).GenerateAsync(AnalysisSamples.Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Generate_ProviderError_DoesNotExposeBodyOrRetry()
    {
        int calls = 0;
        using StubHandler handler = new(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            { Content = new StringContent("sensitive body") });
        });
        using HttpClient client = new(handler);
        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Generator(client).GenerateAsync(AnalysisSamples.Request(), CancellationToken.None));
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.DoesNotContain("sensitive", exception.Message);
        Assert.Equal(1, calls);
    }

    private static OpenAiReportGenerator Generator(HttpClient client) => new(client, Options.Create(new AiOptions
    { ApiKey = "synthetic-key", Model = "configured-model" }));

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
