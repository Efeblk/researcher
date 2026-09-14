using System.Net;
using System.Text;
using System.Text.Json;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ProductPilot;

public sealed class ProductPilotBudgetState
{
    public const string RequiredModel = "gemini-3.8-flash";
    private readonly Func<ProductPilotBudgetSnapshot, Task> persist;
    private readonly int maximumCalls;
    private readonly decimal maximumSpendUsd;
    private readonly object gate = new();
    private readonly List<ProductPilotCall> calls;
    private decimal committedSpendUsd;
    private bool dispatchStopped;

    public ProductPilotBudgetState(
        Func<ProductPilotBudgetSnapshot, Task> persist,
        int maximumCalls = 16,
        decimal maximumSpendUsd = 1m,
        ProductPilotBudgetSnapshot? restored = null)
    {
        this.persist = persist;
        this.maximumCalls = maximumCalls;
        this.maximumSpendUsd = maximumSpendUsd;
        calls = restored?.Items.ToList() ?? [];
        committedSpendUsd = restored?.CommittedSpendUsd ?? 0m;
        dispatchStopped = restored?.DispatchStopped ?? false;
        if (restored is not null)
            ValidateRestored(restored);
    }

    public async Task<ProductPilotReservation> ReserveAsync(
        int bodyBytes, int maximumOutputTokens, string model, DateTime startedAt)
    {
        if (!string.Equals(model, RequiredModel, StringComparison.Ordinal))
            throw new ProductPilotBudgetException("The requested Gemini model is not the exact released model.");
        if (!GeminiUsagePricing.TryGetRates(model, startedAt, out decimal inputRate, out _,
                out decimal outputRate, out string? pricingVersion))
            throw new ProductPilotBudgetException("Gemini pricing is unavailable for the exact model and UTC date.");
        decimal raw = ((bodyBytes + 512m) * inputRate + maximumOutputTokens * outputRate) / 1_000_000m;
        decimal reserved = decimal.Ceiling(raw * 1_000_000_000m) / 1_000_000_000m;
        ProductPilotReservation reservation;
        ProductPilotBudgetSnapshot snapshot;
        lock (gate)
        {
            if (dispatchStopped)
                throw new ProductPilotBudgetException("Dispatch is stopped after unknown or mismatched provider usage.");
            if (calls.Count >= maximumCalls)
                throw new ProductPilotBudgetException("The aggregate 16-call ceiling was reached.");
            if (committedSpendUsd + reserved > maximumSpendUsd)
                throw new ProductPilotBudgetException("The next reservation would exceed the aggregate USD 1.00 ceiling.");
            int ordinal = calls.Count + 1;
            committedSpendUsd += reserved;
            calls.Add(new(ordinal, startedAt, model, bodyBytes, maximumOutputTokens, reserved,
                null, pricingVersion!, null, null, null, false, null));
            reservation = new(ordinal, reserved);
            snapshot = SnapshotUnsafe();
        }
        await persist(snapshot);
        return reservation;
    }

    public async Task CompleteAsync(int ordinal, HttpStatusCode? statusCode, GeminiUsageCompletion? completion)
    {
        ProductPilotBudgetSnapshot snapshot;
        lock (gate)
        {
            ProductPilotCall current = calls[ordinal - 1];
            if (current.Completed) return;
            bool exactModel = string.Equals(completion?.ReturnedModel, RequiredModel, StringComparison.Ordinal);
            bool reliable = completion is { UsageValidForAttribution: true, EstimatedUsd: not null } &&
                exactModel && completion.EstimatedUsd >= 0 && completion.EstimatedUsd <= current.ReservedUsd &&
                string.Equals(completion.PricingVersion, current.PricingVersion, StringComparison.Ordinal);
            string? error = reliable ? null : completion switch
            {
                null => "missing_or_unparseable_response",
                { ReturnedModel: null } => "missing_returned_model",
                _ when !exactModel => "returned_model_mismatch",
                { UsageValidForAttribution: false } => "unreliable_usage",
                { EstimatedUsd: null } => "unpriced_usage",
                _ when completion.EstimatedUsd > current.ReservedUsd => "actual_usage_exceeded_reservation",
                _ => "usage_pricing_mismatch"
            };
            if (reliable)
                committedSpendUsd += completion!.EstimatedUsd!.Value - current.ReservedUsd;
            else
            {
                if (exactModel && completion?.UsageValidForAttribution == true &&
                    completion.EstimatedUsd > current.ReservedUsd &&
                    string.Equals(completion.PricingVersion, current.PricingVersion, StringComparison.Ordinal))
                    committedSpendUsd += completion.EstimatedUsd.Value - current.ReservedUsd;
                dispatchStopped = true;
            }
            calls[ordinal - 1] = current with
            {
                ActualUsd = completion?.EstimatedUsd,
                ReturnedModel = completion?.ReturnedModel,
                HttpStatus = statusCode is null ? null : (int)statusCode,
                Outcome = completion?.Outcome,
                ActualUsageReliable = reliable,
                ReconciliationError = error,
                Completed = true
            };
            snapshot = SnapshotUnsafe();
        }
        await persist(snapshot);
    }

    public ProductPilotBudgetSnapshot Snapshot()
    {
        lock (gate) return SnapshotUnsafe();
    }

    private ProductPilotBudgetSnapshot SnapshotUnsafe() => new(
        maximumCalls, maximumSpendUsd, calls.Count, committedSpendUsd,
        calls.Count(value => value.Completed && !value.ActualUsageReliable), dispatchStopped, calls.ToArray());

    private void ValidateRestored(ProductPilotBudgetSnapshot restored)
    {
        decimal actualSpend = restored.Items.Sum(value => value.ActualUsd ?? 0m);
        bool valid = restored.MaximumCalls == maximumCalls && restored.MaximumSpendUsd == maximumSpendUsd &&
            restored.Calls == restored.Items.Count && restored.Calls <= maximumCalls &&
            restored.CommittedSpendUsd == actualSpend && restored.CommittedSpendUsd <= maximumSpendUsd &&
            restored.UnknownUsageCalls == 0 && !restored.DispatchStopped &&
            restored.Items.Select((value, index) => (value, index)).All(pair =>
                pair.value.Ordinal == pair.index + 1 && pair.value.Completed && pair.value.ActualUsageReliable &&
                pair.value.ReconciliationError is null && pair.value.ActualUsd is >= 0 &&
                pair.value.ActualUsd <= pair.value.ReservedUsd &&
                pair.value.RequestedModel == RequiredModel && pair.value.ReturnedModel == RequiredModel);
        if (!valid)
            throw new ProductPilotBudgetException("The prior budget ledger is not safe to restore.");
    }
}

public sealed class ProductPilotBudgetHandler(ProductPilotBudgetState budget) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        DateTime startedAt = DateTime.UtcNow;
        ValidateTarget(request);
        byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        int maximumOutputTokens = ValidateBody(body);
        ProductPilotReservation reservation = await budget.ReserveAsync(
            body.Length, maximumOutputTokens, ProductPilotBudgetState.RequiredModel, startedAt);
        HttpResponseMessage? response = null;
        GeminiUsageCompletion? completion = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, cancellationToken);
            try
            {
                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                completion = GeminiUsagePricing.Parse(document.RootElement, ProductPilotBudgetState.RequiredModel,
                    startedAt, (int)response.StatusCode, response.IsSuccessStatusCode ? "Observed" : "Rejected");
            }
            catch (JsonException) { }
            return response;
        }
        finally
        {
            await budget.CompleteAsync(reservation.Ordinal, response?.StatusCode, completion);
        }
    }

    private static void ValidateTarget(HttpRequestMessage request)
    {
        string expectedPath = "/v1beta/models/gemini-3.8-flash:generateContent";
        if (request.Method != HttpMethod.Post || request.RequestUri is null ||
            request.RequestUri.Scheme != Uri.UriSchemeHttps || !request.RequestUri.IsDefaultPort ||
            !string.IsNullOrEmpty(request.RequestUri.UserInfo) ||
            !string.Equals(request.RequestUri.Host, "generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.RequestUri.AbsolutePath, expectedPath, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(request.RequestUri.Query) || request.Content is null)
            throw new ProductPilotBudgetException("Only the exact released Gemini generateContent target is allowed.");
    }

    private static int ValidateBody(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement generation = document.RootElement.GetProperty("generationConfig");
            int max = generation.GetProperty("maxOutputTokens").GetInt32();
            string? thinking = generation.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString();
            if (max is not (8192 or 16384) || thinking != "high")
                throw new ProductPilotBudgetException(
                    "Gemini calls must use high thinking and the released 16384 generation or 8192 verification output bound.");
            return max;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException)
        {
            throw new ProductPilotBudgetException("The serialized Gemini settings could not be verified.");
        }
    }
}

public sealed class ProductPilotBudgetException(string message) : HttpRequestException(message);
public sealed record ProductPilotReservation(int Ordinal, decimal ReservedUsd);
public sealed record ProductPilotCall(int Ordinal, DateTime StartedAt, string RequestedModel,
    int BodyBytes, int MaximumOutputTokens, decimal ReservedUsd, decimal? ActualUsd,
    string PricingVersion, string? ReturnedModel, int? HttpStatus, string? Outcome,
    bool ActualUsageReliable, string? ReconciliationError, bool Completed = false);
public sealed record ProductPilotBudgetSnapshot(int MaximumCalls, decimal MaximumSpendUsd,
    int Calls, decimal CommittedSpendUsd, int UnknownUsageCalls, bool DispatchStopped,
    IReadOnlyList<ProductPilotCall> Items);

internal static class ProductPilotBudgetTests
{
    public static async Task<int> RunAsync()
    {
        LiveProductPilot.RunIdentitySelfTest();
        string good = JsonSerializer.Serialize(new
        {
            generationConfig = new { maxOutputTokens = 8192, thinkingConfig = new { thinkingLevel = "high" } }
        });
        string faculty = good.Replace("8192", "16384", StringComparison.Ordinal);
        await ExpectRejectedAsync("wrong model", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", good);
        await ExpectRejectedAsync("wrong output bound", "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent",
            good.Replace("8192", "4096", StringComparison.Ordinal));
        ProductPilotBudgetState state = new(_ => Task.CompletedTask);
        ProductPilotBudgetHandler handler = new(state)
        {
            InnerHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"modelVersion\":\"gemini-other\",\"usageMetadata\":{\"promptTokenCount\":1,\"candidatesTokenCount\":1}}", Encoding.UTF8, "application/json")
            })
        };
        using HttpClient client = new(handler);
        using HttpRequestMessage first = Request("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent", good);
        using HttpResponseMessage ignored = await client.SendAsync(first);
        if (!state.Snapshot().DispatchStopped || state.Snapshot().Items.Single().ReconciliationError != "returned_model_mismatch")
            throw new InvalidOperationException("Returned-model mismatch did not fail closed.");
        using HttpRequestMessage second = Request("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent", good);
        try { await client.SendAsync(second); throw new InvalidOperationException("Stopped guard allowed a second dispatch."); }
        catch (ProductPilotBudgetException) { }

        int persisted = 0;
        ProductPilotBudgetState persistedState = new(_ => { persisted++; return Task.CompletedTask; });
        ProductPilotBudgetHandler persistedHandler = new(persistedState)
        {
            InnerHandler = new StubHandler(_ =>
            {
                if (persisted == 0) throw new InvalidOperationException("Reservation was not persisted before dispatch.");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"modelVersion\":\"gemini-3.8-flash\",\"usageMetadata\":{\"promptTokenCount\":1,\"candidatesTokenCount\":1}}",
                    Encoding.UTF8, "application/json") };
            })
        };
        using (HttpClient persistedClient = new(persistedHandler))
        using (HttpRequestMessage persistedRequest = Request(
                   "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent", good))
        using (HttpResponseMessage persistedResponse = await persistedClient.SendAsync(persistedRequest)) { }
        if (persisted < 2 || persistedState.Snapshot().DispatchStopped)
            throw new InvalidOperationException("Reservation/completion persistence test failed.");

        ProductPilotBudgetState facultyState = new(_ => Task.CompletedTask);
        ProductPilotBudgetHandler facultyHandler = new(facultyState)
        {
            InnerHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"modelVersion\":\"gemini-3.8-flash\",\"usageMetadata\":{\"promptTokenCount\":1,\"candidatesTokenCount\":1}}",
                Encoding.UTF8, "application/json") })
        };
        using (HttpClient facultyClient = new(facultyHandler))
        using (HttpRequestMessage facultyRequest = Request(
                   "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent", faculty))
        using (HttpResponseMessage facultyResponse = await facultyClient.SendAsync(facultyRequest)) { }
        if (facultyState.Snapshot().Items.Single().MaximumOutputTokens != 16384)
            throw new InvalidOperationException("The faculty output bound was not used for reservation.");

        ProductPilotBudgetState unknownState = new(_ => Task.CompletedTask);
        ProductPilotBudgetHandler unknownHandler = new(unknownState)
        { InnerHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }) };
        using (HttpClient unknownClient = new(unknownHandler))
        using (HttpRequestMessage unknownRequest = Request(
                   "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent", good))
        using (HttpResponseMessage unknownResponse = await unknownClient.SendAsync(unknownRequest)) { }
        if (!unknownState.Snapshot().DispatchStopped || unknownState.Snapshot().Items.Single().ReconciliationError != "missing_returned_model")
            throw new InvalidOperationException("Unknown response did not stop dispatch.");

        ProductPilotBudgetState admission = new(_ => Task.CompletedTask, 1, 1m);
        ProductPilotReservation admitted = await admission.ReserveAsync(100, 8192,
            ProductPilotBudgetState.RequiredModel, DateTime.UtcNow);
        await admission.CompleteAsync(admitted.Ordinal, HttpStatusCode.OK, new GeminiUsageCompletion
        {
            UsageValidForAttribution = true, ReturnedModel = ProductPilotBudgetState.RequiredModel,
            PricingVersion = admission.Snapshot().Items[0].PricingVersion, EstimatedUsd = 0.001m, Outcome = "Success"
        });
        try { await admission.ReserveAsync(100, 8192, ProductPilotBudgetState.RequiredModel, DateTime.UtcNow);
            throw new InvalidOperationException("Call ceiling was not enforced."); }
        catch (ProductPilotBudgetException) { }
        ProductPilotBudgetState spend = new(_ => Task.CompletedTask, 16, 0.000001m);
        try { await spend.ReserveAsync(100, 8192, ProductPilotBudgetState.RequiredModel, DateTime.UtcNow);
            throw new InvalidOperationException("Spend ceiling was not enforced."); }
        catch (ProductPilotBudgetException) { }
        ProductPilotBudgetSnapshot prior = new(16, 1m, 1, 0.001m, 0, false,
        [
            new ProductPilotCall(1, DateTime.UtcNow, ProductPilotBudgetState.RequiredModel, 100, 8192,
                0.002m, 0.001m, "test", ProductPilotBudgetState.RequiredModel, 200, "Observed", true, null, true)
        ]);
        ProductPilotBudgetState restored = new(_ => Task.CompletedTask, 16, 1m, prior);
        if (restored.Snapshot().Calls != 1 || restored.Snapshot().CommittedSpendUsd != 0.001m)
            throw new InvalidOperationException("Prior reliable ledger was not restored exactly.");
        try
        {
            _ = new ProductPilotBudgetState(_ => Task.CompletedTask, 16, 1m,
                prior with { CommittedSpendUsd = 0m });
            throw new InvalidOperationException("Invalid prior ledger was restored.");
        }
        catch (ProductPilotBudgetException) { }
        Console.WriteLine("Product pilot guard self-tests passed (target/settings, actual-body reservation, ledger restore, persisted reservation, unknown/mismatch fail-closed, call/spend admission).");
        return 0;
    }

    private static async Task ExpectRejectedAsync(string name, string uri, string body)
    {
        ProductPilotBudgetState state = new(_ => Task.CompletedTask);
        ProductPilotBudgetHandler handler = new(state) { InnerHandler = new StubHandler(_ => throw new InvalidOperationException()) };
        using HttpClient client = new(handler); using HttpRequestMessage request = Request(uri, body);
        try { await client.SendAsync(request); throw new InvalidOperationException(name + " was accepted."); }
        catch (ProductPilotBudgetException) { }
        if (state.Snapshot().Calls != 0) throw new InvalidOperationException(name + " consumed budget.");
    }

    private static HttpRequestMessage Request(string uri, string body) => new(HttpMethod.Post, uri)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request)); }
}
