using System.Net;
using System.Text;

namespace ServiceAcceptancePilot;

internal static class ServiceAcceptanceSelfTest
{
    public static async Task<int> RunAsync()
    {
        await AssertAsync();
        Console.WriteLine("SELF_TEST_OK");
        return 0;
    }

    public static async Task AssertAsync()
    {
        int persisted = 0;
        ServiceAcceptanceBudget exact = new(_ => { persisted++; return Task.CompletedTask; });
        using (HttpClient client = Client(exact, GoodResponse(ServiceAcceptanceBudget.RequiredModel)))
        using (HttpResponseMessage response = await client.SendAsync(Request(8192))) { }
        ServiceAcceptanceBudgetSnapshot exactSnapshot = exact.Snapshot();
        Require(persisted == 2 && exactSnapshot.Calls == 1 && exactSnapshot.UnknownUsageCalls == 0 &&
            !exactSnapshot.DispatchStopped && exactSnapshot.Items.Single().ReturnedModel ==
                ServiceAcceptanceBudget.RequiredModel,
            "Exact response was not reconciled and persisted.");

        ServiceAcceptanceBudget mismatch = new(_ => Task.CompletedTask);
        using (HttpClient client = Client(mismatch, GoodResponse("gemini-other")))
        using (HttpResponseMessage response = await client.SendAsync(Request(16384))) { }
        Require(mismatch.Snapshot().DispatchStopped &&
            mismatch.Snapshot().Items.Single().ReconciliationError == "returned_model_mismatch",
            "Returned model mismatch did not stop dispatch.");
        await ExpectBudgetFailureAsync(() => Client(mismatch, GoodResponse(
            ServiceAcceptanceBudget.RequiredModel)).SendAsync(Request(8192)));

        ServiceAcceptanceBudget unknown = new(_ => Task.CompletedTask);
        using (HttpClient client = Client(unknown, new HttpResponseMessage(HttpStatusCode.OK)
               { Content = new StringContent("{}", Encoding.UTF8, "application/json") }))
        using (HttpResponseMessage response = await client.SendAsync(Request(8192))) { }
        ServiceAcceptanceBudget restored = new(_ => Task.CompletedTask, unknown.Snapshot());
        await ExpectBudgetFailureAsync(() => Client(restored, GoodResponse(
            ServiceAcceptanceBudget.RequiredModel)).SendAsync(Request(8192)));

        ServiceAcceptanceBudgetSnapshot reliable = exact.Snapshot();
        ServiceAcceptanceBudgetSnapshot tampered = reliable with { CommittedSpendUsd = 0m };
        try
        {
            _ = new ServiceAcceptanceBudget(_ => Task.CompletedTask, tampered);
            throw new InvalidOperationException("A tampered undercounted ledger was restored.");
        }
        catch (ServiceAcceptanceBudgetException) { }

        int activePersists = 0;
        int maximumActivePersists = 0;
        List<int> persistedCalls = [];
        ServiceAcceptanceBudget concurrent = new(async snapshot =>
        {
            int active = Interlocked.Increment(ref activePersists);
            maximumActivePersists = Math.Max(maximumActivePersists, active);
            await Task.Delay(10);
            lock (persistedCalls) persistedCalls.Add(snapshot.Calls);
            Interlocked.Decrement(ref activePersists);
        });
        ServiceAcceptanceReservation[] reservations = await Task.WhenAll(
            concurrent.ReserveAsync(100, 8192, ServiceAcceptanceBudget.RequiredModel, DateTime.UtcNow),
            concurrent.ReserveAsync(100, 8192, ServiceAcceptanceBudget.RequiredModel, DateTime.UtcNow));
        Require(maximumActivePersists == 1 && persistedCalls.SequenceEqual([1, 2]),
            "Concurrent reservations were not persisted in order.");
        string pricing = concurrent.Snapshot().Items[0].PricingVersion;
        await Task.WhenAll(reservations.Select(value => concurrent.CompleteAsync(value.Ordinal,
            HttpStatusCode.OK, new()
            {
                UsageValidForAttribution = true,
                ReturnedModel = ServiceAcceptanceBudget.RequiredModel,
                EstimatedUsd = 0.00001m,
                PricingVersion = pricing,
                Outcome = "Success"
            })));
        Require(maximumActivePersists == 1 && concurrent.Snapshot().UnknownUsageCalls == 0,
            "Concurrent completions raced ledger persistence.");

        int downstream = 0;
        ServiceAcceptanceBudget invalid = new(_ => Task.CompletedTask);
        using HttpClient invalidClient = new(new ServiceAcceptanceBudgetHandler(invalid)
        {
            InnerHandler = new StubHandler(_ => { downstream++; return GoodResponse(ServiceAcceptanceBudget.RequiredModel); })
        });
        using HttpRequestMessage wrong = Request(4096);
        await ExpectBudgetFailureAsync(() => invalidClient.SendAsync(wrong));
        Require(downstream == 0, "Invalid serialized settings reached the outbound handler.");
    }

    private static HttpClient Client(ServiceAcceptanceBudget budget, HttpResponseMessage response) =>
        new(new ServiceAcceptanceBudgetHandler(budget)
        {
            InnerHandler = new StubHandler(_ => response)
        });

    private static HttpRequestMessage Request(int maximumOutputTokens) => new(HttpMethod.Post,
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent")
    {
        Content = new StringContent("{\"generationConfig\":{\"maxOutputTokens\":" + maximumOutputTokens +
            ",\"thinkingConfig\":{\"thinkingLevel\":\"high\"}}}", Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage GoodResponse(string model) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"modelVersion\":\"" + model +
            "\",\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":5,\"thoughtsTokenCount\":2,\"totalTokenCount\":17}}",
            Encoding.UTF8, "application/json")
    };

    private static async Task ExpectBudgetFailureAsync(Func<Task<HttpResponseMessage>> action)
    {
        try
        {
            using HttpResponseMessage ignored = await action();
            throw new InvalidOperationException("The budget guard allowed a forbidden dispatch.");
        }
        catch (ServiceAcceptanceBudgetException) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
