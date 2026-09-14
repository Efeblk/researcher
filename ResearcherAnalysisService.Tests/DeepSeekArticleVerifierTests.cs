using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.DeepSeek;

namespace ResearcherAnalysisService.Tests;

public sealed class DeepSeekArticleVerifierTests
{
    [Fact]
    public async Task ClaimVerifier_BatchedClaimsContainOnlyPerItemCitations()
    {
        IReadOnlyList<ArticleSourceSpan> spans =
        [
            new("s0", 1, 0, 8, "Uncited neighbor."),
            new("s1", 1, 8, 24, "Adam combines AdaGrad."),
            new("s2", 1, 24, 38, "Adam also combines RMSProp.")
        ];
        IReadOnlyList<GeneratedArticleClaim> claims =
        [
            new("c1", "Adam combines AdaGrad and RMSProp.", ["s1"]),
            new("c2", "Adam combines AdaGrad and RMSProp.", ["s1", "s2"]),
            new("c3", "Adam includes RMSProp.", ["s2"])
        ];
        ArticleEvaluationAttemptRecorder recorder = new(1);
        using StubHandler handler = new(async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            using JsonDocument input = UserInput(body);
            JsonElement items = input.RootElement.GetProperty("items");
            Assert.Equal(["s1"], SourceIds(items[0]));
            Assert.Equal(["s1", "s2"], SourceIds(items[1]));
            Assert.Equal(["s2"], SourceIds(items[2]));
            Assert.DoesNotContain("Uncited neighbor", input.RootElement.GetRawText());
            Assert.False(input.RootElement.TryGetProperty("sources", out _));
            return Response("{\"verdicts\":[{\"claimId\":\"c1\",\"verdict\":\"uncertain\",\"reason\":\"Missing support.\"},{\"claimId\":\"c2\",\"verdict\":\"supported\",\"reason\":\"Complete support.\"},{\"claimId\":\"c3\",\"verdict\":\"supported\",\"reason\":\"Direct support.\"}]}");
        });
        DeepSeekArticleClaimVerifier verifier = new(Client(handler, recorder), 1024);

        GeneratedVerificationBatch result;
        using (recorder.Enter("calibration_verify", null))
            result = await verifier.VerifyAsync("en", claims, spans, default);

        Assert.Equal(["uncertain", "supported", "supported"], result.Verdicts.Select(value => value.Verdict));
    }

    [Fact]
    public async Task ReviewVerifier_BatchedFindingsContainOnlyPerItemCitations()
    {
        IReadOnlyList<ArticleSourceSpan> spans =
        [
            new("s0", 1, 0, 8, "Uncited neighbor."),
            new("s1", 1, 8, 24, "The authors used 12 samples."),
            new("s2", 1, 24, 38, "Yazarlar nedensellik iddia etmedi.")
        ];
        IReadOnlyList<GeneratedArticleReviewFinding> findings =
        [
            new("f1", "method", "source_observation", "The authors used 12 samples.", null, ["s1"]),
            new("f2", "method", "source_observation", "Yazarlar nedensellik iddia etmedi.", null, ["s2"])
        ];
        ArticleEvaluationAttemptRecorder recorder = new(1);
        using StubHandler handler = new(async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            using JsonDocument input = UserInput(body);
            JsonElement items = input.RootElement.GetProperty("items");
            Assert.Equal(["s1"], SourceIds(items[0]));
            Assert.Equal(["s2"], SourceIds(items[1]));
            Assert.DoesNotContain("Uncited neighbor", input.RootElement.GetRawText());
            Assert.False(input.RootElement.TryGetProperty("sources", out _));
            return Response("{\"verdicts\":[{\"findingId\":\"f1\",\"verdict\":\"supported\",\"reason\":\"Direct support.\"},{\"findingId\":\"f2\",\"verdict\":\"supported\",\"reason\":\"Direct support.\"}]}");
        });
        DeepSeekArticleReviewVerifier verifier = new(Client(handler, recorder), 1024);

        GeneratedArticleReviewVerification result;
        using (recorder.Enter("review_verify", "method"))
            result = await verifier.VerifyAsync("method", "tr", findings, spans, default);

        Assert.All(result.Verdicts, verdict => Assert.Equal("supported", verdict.Verdict));
    }

    private static DeepSeekArticleClient Client(HttpMessageHandler handler,
        ArticleEvaluationAttemptRecorder recorder) => new(new HttpClient(handler),
        Options.Create(new ArticleEvaluationOptions { DeepSeek = new() { ApiKey = "synthetic-key" } }), recorder);

    private static JsonDocument UserInput(JsonDocument body) => JsonDocument.Parse(body.RootElement
        .GetProperty("messages")[1].GetProperty("content").GetString()!);

    private static string[] SourceIds(JsonElement item) => item.GetProperty("sources").EnumerateArray()
        .Select(source => source.GetProperty("sourceId").GetString()!).ToArray();

    private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            choices = new[] { new { finish_reason = "stop", message = new { content } } },
            model = "deepseek-v4.1-flash-20260910",
            usage = new
            {
                prompt_tokens = 100,
                completion_tokens = 25,
                prompt_cache_hit_tokens = 40,
                prompt_cache_miss_tokens = 60,
                total_tokens = 125
            }
        })
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
