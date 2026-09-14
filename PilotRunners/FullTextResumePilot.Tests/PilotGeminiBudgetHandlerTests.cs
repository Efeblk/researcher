using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using FullTextResumePilot;
using Microsoft.Extensions.DependencyInjection;
using ResearcherAnalysisService.Integrations.Gemini;
using Xunit;

namespace FullTextResumePilot.Tests;

public sealed class PilotGeminiBudgetHandlerTests
{
    [Fact]
    public async Task SendAsync_ValidUsage_ReplacesReservationWithActualCost()
    {
        PilotGeminiBudgetState state = new();
        using HttpClient client = Client(state, _ => Response(10, 2, 3));

        using HttpResponseMessage response = await client.SendAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PilotGeminiBudgetSnapshot snapshot = state.Snapshot();
        Assert.Equal(1, snapshot.Calls);
        Assert.Equal(0, snapshot.UnknownUsageCalls);
        Assert.Equal(0.00002625m, snapshot.CommittedSpendUsd);
        Assert.True(snapshot.Items[0].ActualUsageReliable);
    }

    [Fact]
    public async Task SendAsync_UnknownUsage_RetainsFullReservation()
    {
        PilotGeminiBudgetState state = new();
        using HttpClient client = Client(state, _ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        using HttpResponseMessage response = await client.SendAsync(Request(), TestContext.Current.CancellationToken);

        PilotGeminiBudgetSnapshot snapshot = state.Snapshot();
        Assert.Equal(snapshot.Items[0].ReservedUsd, snapshot.CommittedSpendUsd);
        Assert.Equal(1, snapshot.UnknownUsageCalls);
    }

    [Fact]
    public async Task SendAsync_NextReservationOverCeiling_NeverCallsInnerHandler()
    {
        int outbound = 0;
        PilotGeminiBudgetState state = new(maximumCalls: 1, maximumSpendUsd: 1m);
        using HttpClient client = Client(state, _ =>
        {
            outbound++;
            return Response(10, 2, 3);
        });
        using HttpResponseMessage first = await client.SendAsync(Request(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<PilotBudgetException>(() =>
            client.SendAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(1, outbound);
        Assert.Equal(1, state.Snapshot().Calls);
    }

    [Fact]
    public async Task SendAsync_ProjectedSpendOverCeiling_NeverCallsInnerHandler()
    {
        int outbound = 0;
        PilotGeminiBudgetState state = new(maximumCalls: 32, maximumSpendUsd: 0.000001m);
        using HttpClient client = Client(state, _ =>
        {
            outbound++;
            return Response(10, 2, 3);
        });

        await Assert.ThrowsAsync<PilotBudgetException>(() =>
            client.SendAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(0, outbound);
        Assert.Equal(0, state.Snapshot().Calls);
    }

    [Fact]
    public async Task SendAsync_NonGeminiHost_IsRejectedBeforeDispatch()
    {
        int outbound = 0;
        PilotGeminiBudgetState state = new();
        using HttpClient client = Client(state, _ =>
        {
            outbound++;
            return Response(10, 2, 3);
        });
        HttpRequestMessage request = Request();
        request.RequestUri = new("https://example.com/v1beta/models/gemini-3.8-flash:generateContent");

        await Assert.ThrowsAsync<PilotBudgetException>(() =>
            client.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(0, outbound);
        Assert.Equal(0, state.Snapshot().Calls);
    }

    [Fact]
    public void Complete_SecondCall_DoesNotApplyActualCreditTwice()
    {
        PilotGeminiBudgetState state = new();
        DateTime now = DateTime.UtcNow;
        PilotGeminiReservation reservation = state.Reserve(100, 8192, "gemini-3.8-flash", now);
        ResearcherAnalysisService.Integrations.Gemini.GeminiUsageCompletion completion = new()
        {
            UsageValidForAttribution = true,
            ReturnedModel = "gemini-3.8-flash",
            PricingVersion = "gemini-3.8-flash-standard-through-2026-12-31",
            EstimatedUsd = 0.01m
        };

        state.Complete(reservation.Ordinal, HttpStatusCode.OK, completion);
        decimal once = state.Snapshot().CommittedSpendUsd;
        state.Complete(reservation.Ordinal, HttpStatusCode.OK, completion);

        Assert.Equal(once, state.Snapshot().CommittedSpendUsd);
    }

    [Fact]
    public async Task SendAsync_ActualOverReservation_StopsFurtherDispatch()
    {
        int outbound = 0;
        PilotGeminiBudgetState state = new();
        using HttpClient client = Client(state, _ =>
        {
            outbound++;
            return Response(10, 100000, 0);
        });
        using HttpResponseMessage first = await client.SendAsync(Request(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<PilotBudgetException>(() =>
            client.SendAsync(Request(), TestContext.Current.CancellationToken));

        PilotGeminiBudgetSnapshot snapshot = state.Snapshot();
        Assert.True(snapshot.DispatchStopped);
        Assert.Equal("actual_usage_exceeded_reservation", snapshot.Items[0].ReconciliationError);
        Assert.Equal(1, outbound);
    }

    [Fact]
    public async Task Reserve_ConcurrentAttempts_AtomicallyAdmitsOnlyCallCeiling()
    {
        PilotGeminiBudgetState state = new(maximumCalls: 32, maximumSpendUsd: 10m);
        DateTime now = DateTime.UtcNow;
        int admitted = 0;

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            try
            {
                state.Reserve(100, 512, "gemini-3.8-flash", now);
                Interlocked.Increment(ref admitted);
            }
            catch (PilotBudgetException) { }
        }, TestContext.Current.CancellationToken)));

        Assert.Equal(32, admitted);
        Assert.Equal(32, state.Snapshot().Calls);
    }

    [Fact]
    public void Complete_WrongModelMetadata_RetainsReservationWithoutCredit()
    {
        PilotGeminiBudgetState state = new();
        PilotGeminiReservation reservation = state.Reserve(100, 8192, "gemini-3.8-flash", DateTime.UtcNow);
        decimal reserved = state.Snapshot().CommittedSpendUsd;

        state.Complete(reservation.Ordinal, HttpStatusCode.OK, new()
        {
            UsageValidForAttribution = true,
            ReturnedModel = "unexpected-model",
            PricingVersion = "gemini-3.8-flash-standard-through-2026-12-31",
            EstimatedUsd = 0.001m
        });

        PilotGeminiBudgetSnapshot snapshot = state.Snapshot();
        Assert.Equal(reserved, snapshot.CommittedSpendUsd);
        Assert.False(snapshot.Items[0].ActualUsageReliable);
        Assert.Equal("usage_pricing_or_model_mismatch", snapshot.Items[0].ReconciliationError);
    }

    [Fact]
    public void AddGuardedClients_TypedProductionAndNamedEvaluationPathsContainBudgetHandler()
    {
        ServiceCollection services = new();
        services.AddSingleton(new PilotGeminiBudgetState(64, 2m));
        services.AddTransient<PilotGeminiBudgetHandler>();
        PilotGeminiClientRegistration.AddGuardedClients(services);
        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpMessageHandlerFactory factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        HttpMessageHandler production = factory.CreateHandler(nameof(GeminiArticleClient));
        HttpMessageHandler evaluation = factory.CreateHandler(PilotGeminiClientRegistration.EvaluationClientName);

        Assert.True(ContainsBudgetHandler(production));
        Assert.True(ContainsBudgetHandler(evaluation));
    }

    [Fact]
    public void Restore_ValidSnapshot_ContinuesSameGlobalCeilingAndOrdinal()
    {
        PilotGeminiBudgetState original = new(64, 2m);
        original.Reserve(100, 8192, "gemini-3.8-flash", DateTime.UtcNow);

        PilotGeminiBudgetState restored = PilotGeminiBudgetState.Restore(original.Snapshot());
        PilotGeminiReservation next = restored.Reserve(100, 8192, "gemini-3.8-flash", DateTime.UtcNow);

        Assert.Equal(2, next.Ordinal);
        Assert.Equal(64, restored.Snapshot().MaximumCalls);
        Assert.Equal(2m, restored.Snapshot().MaximumSpendUsd);
    }

    [Fact]
    public void ValidateResumeSnapshot_CompletedReliableLedgerAndSqlMatch_IsAccepted()
    {
        PilotGeminiBudgetState state = CompletedAcceptanceState();
        PilotGeminiBudgetSnapshot snapshot = state.Snapshot();

        PilotGeminiBudgetState.ValidateResumeSnapshot(snapshot, 1, snapshot.CommittedSpendUsd, false);
    }

    [Fact]
    public void ValidateResumeSnapshot_PendingOrSqlMismatch_IsRejected()
    {
        PilotGeminiBudgetState pending = new(64, 2m);
        pending.Reserve(100, 8192, "gemini-3.8-flash", DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => PilotGeminiBudgetState.ValidateResumeSnapshot(
            pending.Snapshot(), 0, 0m, false));

        PilotGeminiBudgetSnapshot completed = CompletedAcceptanceState().Snapshot();
        Assert.Throws<InvalidOperationException>(() => PilotGeminiBudgetState.ValidateResumeSnapshot(
            completed, 1, completed.CommittedSpendUsd + 0.01m, false));
    }

    [Fact]
    public void EquivalentSourceUrl_PercentEncodedAndDecodedSpaces_AreEqual()
    {
        Assert.True(LivePilot.EquivalentSourceUrl(
            "https://livrepository.liverpool.ac.uk/3166141/1/Multiagent off-screen behavior prediction in football.pdf",
            "https://livrepository.liverpool.ac.uk/3166141/1/Multiagent%20off-screen%20behavior%20prediction%20in%20football.pdf"));
    }

    [Theory]
    [InlineData("https://example.test/a%2Fb", "https://example.test/a/b")]
    [InlineData("https://example.test/Paper.pdf", "https://example.test/paper.pdf")]
    [InlineData("https://example.test/paper.pdf?v=1", "https://example.test/paper.pdf?v=2")]
    public void EquivalentSourceUrl_DifferentReservedPathCaseOrQuery_IsNotEqual(string left, string right)
    {
        Assert.False(LivePilot.EquivalentSourceUrl(left, right));
    }

    [Fact]
    public void ReportComparer_PascalAndCamelCaseSameTypedReport_AreEqual()
    {
        JsonNode pascal = JsonNode.Parse("""
            {"TextCoverage":"provided_pdf_pages_text","FormulaAndTableLayoutVerified":false,"FigureImageryAnalyzed":false,"Limitation":"same"}
            """)!;
        JsonNode camel = JsonNode.Parse("""
            {"textCoverage":"provided_pdf_pages_text","formulaAndTableLayoutVerified":false,"figureImageryAnalyzed":false,"limitation":"same"}
            """)!;

        Assert.True(PilotReportComparer.Equivalent<ArticleSourceFidelity>(pascal, camel));
    }

    [Fact]
    public void ReportComparer_MismatchedTypedValue_IsNotEqual()
    {
        JsonNode left = JsonNode.Parse("""
            {"TextCoverage":"provided_pdf_pages_text","FormulaAndTableLayoutVerified":false,"FigureImageryAnalyzed":false,"Limitation":"first"}
            """)!;
        JsonNode right = JsonNode.Parse("""
            {"textCoverage":"provided_pdf_pages_text","formulaAndTableLayoutVerified":false,"figureImageryAnalyzed":false,"limitation":"second"}
            """)!;

        Assert.False(PilotReportComparer.Equivalent<ArticleSourceFidelity>(left, right));
    }

    [Fact]
    public void ReportComparer_OmittedOptionalNullAndExplicitNull_AreEqual()
    {
        JsonNode omitted = JsonNode.Parse("""
            {"FindingId":"method:f1","Role":"method","Kind":"source_observation","Basis":"basis","Evidence":[]}
            """)!;
        JsonNode explicitNull = JsonNode.Parse("""
            {"findingId":"method:f1","role":"method","kind":"source_observation","basis":"basis","suggestion":null,"evidence":[]}
            """)!;

        Assert.True(PilotReportComparer.Equivalent<ArticleReviewFinding>(omitted, explicitNull));
    }

    [Fact]
    public void JsonNodeDeepEquals_PropertyNamesDifferOnlyByCase_AreNotEqual()
    {
        Assert.False(JsonNode.DeepEquals(JsonNode.Parse("{\"Value\":1}"), JsonNode.Parse("{\"value\":1}")));
    }

    private static HttpClient Client(PilotGeminiBudgetState state,
        Func<HttpRequestMessage, HttpResponseMessage> responder) => new(new PilotGeminiBudgetHandler(state)
        {
            InnerHandler = new StubHandler(responder)
        });

    private static HttpRequestMessage Request() => new(HttpMethod.Post,
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent")
    {
        Content = new StringContent("{\"generationConfig\":{\"maxOutputTokens\":8192}}",
            Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Response(long prompt, long candidates, long thoughts) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                modelVersion = "gemini-3.8-flash",
                usageMetadata = new
                {
                    promptTokenCount = prompt,
                    candidatesTokenCount = candidates,
                    thoughtsTokenCount = thoughts,
                    totalTokenCount = prompt + candidates + thoughts
                }
            }), Encoding.UTF8, "application/json")
        };

    private static bool ContainsBudgetHandler(HttpMessageHandler handler)
    {
        for (HttpMessageHandler? current = handler; current is not null;
             current = (current as DelegatingHandler)?.InnerHandler)
            if (current is PilotGeminiBudgetHandler) return true;
        return false;
    }

    private static PilotGeminiBudgetState CompletedAcceptanceState()
    {
        PilotGeminiBudgetState state = new(64, 2m);
        PilotGeminiReservation reservation = state.Reserve(100, 8192, "gemini-3.8-flash", DateTime.UtcNow);
        state.Complete(reservation.Ordinal, HttpStatusCode.OK, new()
        {
            UsageValidForAttribution = true,
            ReturnedModel = "gemini-3.8-flash",
            PricingVersion = "gemini-3.8-flash-standard-through-2026-12-31",
            EstimatedUsd = 0.01m
        });
        return state;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
