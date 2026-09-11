using System.Net;
using System.Text;
using System.Text.Json;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class ProviderStatusEndpointTests
{
    [Fact]
    public async Task Gemini_ConfiguredModel_ReturnsSanitizedHealthyStatus()
    {
        const string secret = "synthetic-gemini-secret";
        using RecordingHandler handler = new(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1beta/models/gemini-test", request.RequestUri!.AbsolutePath);
            Assert.Equal(secret, request.Headers.GetValues("x-goog-api-key").Single());
            Assert.Null(request.Content);
            return Json("""
                {"name":"models/gemini-test","supportedGenerationMethods":["generateContent"]}
                """);
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(geminiHandler: handler,
            settings: new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = secret,
                ["Ai:ArticleProvider"] = "Gemini",
                ["Ai:ArticleModel"] = "gemini-test"
            });

        using HttpResponseMessage response = await host.Client.GetAsync("/api/v1/internal/provider-status/gemini");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        Assert.Equal(["provider", "health", "quotas"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Gemini", document.RootElement.GetProperty("provider").GetString());
        Assert.Equal("Healthy", document.RootElement.GetProperty("health").GetString());
        Assert.Empty(document.RootElement.GetProperty("quotas").EnumerateArray());
        Assert.DoesNotContain(secret, body);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("Ollama", "key", "model", "Disabled")]
    [InlineData("Gemini", null, "model", "NotConfigured")]
    [InlineData("Gemini", "key", "", "NotConfigured")]
    public async Task Gemini_DisabledOrMissingConfiguration_SkipsUpstream(
        string provider, string? key, string model, string expectedHealth)
    {
        using RecordingHandler handler = new(_ => throw new InvalidOperationException("Must not be called."));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(geminiHandler: handler,
            settings: new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = key,
                ["Ai:ArticleProvider"] = provider,
                ["Ai:ArticleModel"] = model
            });

        using HttpResponseMessage response = await host.Client.GetAsync("/api/v1/internal/provider-status/gemini");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"\"health\":\"{expectedHealth}\"", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unavailable")]
    public async Task Gemini_ProviderFailure_ReturnsSanitizedHealth(HttpStatusCode upstream, string expectedHealth)
    {
        const string sensitive = "sensitive-upstream-detail";
        using RecordingHandler handler = new(_ => new HttpResponseMessage(upstream)
        {
            Content = new StringContent(sensitive)
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(geminiHandler: handler,
            settings: ConfiguredSettings());

        using HttpResponseMessage response = await host.Client.GetAsync("/api/v1/internal/provider-status/gemini");

        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"\"health\":\"{expectedHealth}\"", body);
        Assert.DoesNotContain(sensitive, body);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"models/other\",\"supportedGenerationMethods\":[\"generateContent\"]}")]
    [InlineData("{\"name\":\"models/gemini-test\",\"supportedGenerationMethods\":[\"countTokens\"]}")]
    [InlineData("sensitive-invalid-json")]
    public async Task Gemini_InvalidMetadata_ReturnsUnexpectedResponseWithoutPayload(string payload)
    {
        using RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(geminiHandler: handler,
            settings: ConfiguredSettings());

        using HttpResponseMessage response = await host.Client.GetAsync("/api/v1/internal/provider-status/gemini");

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"health\":\"UnexpectedResponse\"", body);
        Assert.DoesNotContain(payload, body);
    }

    [Fact]
    public async Task Gemini_InvalidAnalysisAccessKey_DoesNotCallProvider()
    {
        using RecordingHandler handler = new(_ => throw new InvalidOperationException("Must not be called."));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(geminiHandler: handler,
            settings: ConfiguredSettings());
        host.Client.DefaultRequestHeaders.Remove("X-Analysis-Key");

        using HttpResponseMessage response = await host.Client.GetAsync("/api/v1/internal/provider-status/gemini");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Gemini_Timeout_ReturnsTimeoutWithoutExceptionDetail()
    {
        using RecordingHandler handler = new(_ => throw new TaskCanceledException("sensitive timeout"));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(geminiHandler: handler,
            settings: ConfiguredSettings());

        using HttpResponseMessage response = await host.Client.GetAsync("/api/v1/internal/provider-status/gemini");

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"health\":\"Timeout\"", body);
        Assert.DoesNotContain("sensitive", body);
    }

    private static Dictionary<string, string?> ConfiguredSettings() => new()
    {
        ["Gemini:ApiKey"] = "synthetic-gemini-key",
        ["Ai:ArticleProvider"] = "Gemini",
        ["Ai:ArticleModel"] = "gemini-test"
    };

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response(request));
        }
    }
}
