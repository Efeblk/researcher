using System.Net;
using System.Text;
using System.Text.Json;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ServiceAcceptancePilot;

public sealed class ServiceAcceptanceBudget
{
    public const string RequiredModel = "gemini-3.8-flash";
    public const int MaximumCalls = 128;
    public const decimal MaximumSpendUsd = 5.00m;
    private readonly Func<ServiceAcceptanceBudgetSnapshot, Task> persist;
    private readonly int maximumCalls;
    private readonly decimal maximumSpendUsd;
    private readonly object gate = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly List<ServiceAcceptanceCall> calls;
    private decimal committedSpendUsd;
    private bool dispatchStopped;
    private string phase = "initialized";

    public ServiceAcceptanceBudget(Func<ServiceAcceptanceBudgetSnapshot, Task> persist,
        ServiceAcceptanceBudgetSnapshot? restored = null, int maximumCalls = MaximumCalls,
        decimal maximumSpendUsd = MaximumSpendUsd)
    {
        if (maximumCalls <= 0 || maximumCalls > MaximumCalls || maximumSpendUsd <= 0 ||
            maximumSpendUsd > MaximumSpendUsd)
            throw new ArgumentOutOfRangeException(nameof(maximumCalls), "Acceptance limits must be positive and no larger than the released aggregate ceiling.");
        this.persist = persist;
        this.maximumCalls = maximumCalls;
        this.maximumSpendUsd = maximumSpendUsd;
        calls = restored?.Items.ToList() ?? [];
        committedSpendUsd = restored?.CommittedSpendUsd ?? 0m;
        dispatchStopped = restored?.DispatchStopped == true || calls.Any(value =>
            !value.Completed || !value.ActualUsageReliable);
        if (restored is not null) ValidateRestored(restored, maximumCalls, maximumSpendUsd);
    }

    public void SetPhase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
            throw new ArgumentException("A bounded non-empty phase is required.", nameof(value));
        lock (gate) phase = value;
    }

    public async Task<ServiceAcceptanceReservation> ReserveAsync(
        int bodyBytes, int maximumOutputTokens, string model, DateTime startedAt)
    {
        if (!string.Equals(model, RequiredModel, StringComparison.Ordinal))
            throw new ServiceAcceptanceBudgetException("The requested model is not exact gemini-3.8-flash.");
        if (!GeminiUsagePricing.TryGetRates(model, startedAt, out decimal inputRate, out _,
                out decimal outputRate, out string? pricingVersion))
            throw new ServiceAcceptanceBudgetException("Pricing is unavailable for the exact model and UTC date.");
        decimal raw = ((bodyBytes + 512m) * inputRate + maximumOutputTokens * outputRate) / 1_000_000m;
        decimal reserved = decimal.Ceiling(raw * 1_000_000_000m) / 1_000_000_000m;
        await operationGate.WaitAsync();
        try
        {
            ServiceAcceptanceReservation reservation;
            ServiceAcceptanceBudgetSnapshot snapshot;
            lock (gate)
            {
                if (dispatchStopped)
                    throw new ServiceAcceptanceBudgetException("Dispatch is stopped after incomplete or unknown usage.");
                if (calls.Count >= maximumCalls)
                    throw new ServiceAcceptanceBudgetException($"The aggregate {maximumCalls}-call ceiling was reached.");
                if (committedSpendUsd + reserved > maximumSpendUsd)
                    throw new ServiceAcceptanceBudgetException(
                        $"The next reservation would exceed aggregate USD {maximumSpendUsd:F2}.");
                committedSpendUsd += reserved;
                calls.Add(new(calls.Count + 1, phase, startedAt, model, bodyBytes, maximumOutputTokens,
                    reserved, null, pricingVersion!, null, null, null, false, null, false));
                reservation = new(calls.Count, reserved);
                snapshot = SnapshotUnsafe();
            }
            await persist(snapshot);
            return reservation;
        }
        finally { operationGate.Release(); }
    }

    public async Task CompleteAsync(int ordinal, HttpStatusCode? status, GeminiUsageCompletion? completion)
    {
        await operationGate.WaitAsync();
        try
        {
            ServiceAcceptanceBudgetSnapshot snapshot;
            lock (gate)
            {
                ServiceAcceptanceCall current = calls[ordinal - 1];
                if (current.Completed) return;
                bool exactModel = completion?.ReturnedModel == RequiredModel;
                bool reliable = completion is { UsageValidForAttribution: true, EstimatedUsd: not null } &&
                    exactModel && completion.EstimatedUsd >= 0 && completion.EstimatedUsd <= current.ReservedUsd &&
                    completion.PricingVersion == current.PricingVersion;
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
                else if (completion?.EstimatedUsd > current.ReservedUsd)
                    committedSpendUsd += completion.EstimatedUsd.Value - current.ReservedUsd;
                dispatchStopped |= !reliable;
                calls[ordinal - 1] = current with
                {
                    ActualUsd = completion?.EstimatedUsd,
                    ReturnedModel = completion?.ReturnedModel,
                    HttpStatus = status is null ? null : (int)status,
                    Outcome = completion?.Outcome,
                    ActualUsageReliable = reliable,
                    ReconciliationError = error,
                    Completed = true
                };
                snapshot = SnapshotUnsafe();
            }
            await persist(snapshot);
        }
        finally { operationGate.Release(); }
    }

    public ServiceAcceptanceBudgetSnapshot Snapshot()
    {
        lock (gate) return SnapshotUnsafe();
    }

    private ServiceAcceptanceBudgetSnapshot SnapshotUnsafe() => new(
        maximumCalls, maximumSpendUsd, calls.Count, committedSpendUsd,
        calls.Count(value => !value.Completed || !value.ActualUsageReliable), dispatchStopped, calls.ToArray());

    private static void ValidateRestored(ServiceAcceptanceBudgetSnapshot value, int maximumCalls,
        decimal maximumSpendUsd)
    {
        decimal recomputed = value.Items.Sum(item => item.Completed && item.ActualUsageReliable
            ? item.ActualUsd!.Value
            : Math.Max(item.ReservedUsd, item.ActualUsd ?? item.ReservedUsd));
        int unknown = value.Items.Count(item => !item.Completed || !item.ActualUsageReliable);
        if (value.MaximumCalls != maximumCalls || value.MaximumSpendUsd != maximumSpendUsd ||
            value.Calls != value.Items.Count || value.Calls > maximumCalls ||
            value.CommittedSpendUsd < 0 || value.CommittedSpendUsd > maximumSpendUsd ||
            value.CommittedSpendUsd != recomputed || value.UnknownUsageCalls != unknown ||
            value.DispatchStopped != (unknown > 0) ||
            value.Items.Select((item, index) => item.Ordinal == index + 1).Any(valid => !valid) ||
            value.Items.Any(item => string.IsNullOrWhiteSpace(item.Phase) || item.Phase.Length > 100 ||
                item.RequestedModel != RequiredModel || item.BodyBytes <= 0 ||
                item.MaximumOutputTokens is not (8192 or 16384) || item.ReservedUsd < 0 ||
                string.IsNullOrWhiteSpace(item.PricingVersion) || item.ActualUsd < 0 ||
                item.Completed && item.ActualUsageReliable && (item.ActualUsd is null ||
                    item.ActualUsd > item.ReservedUsd || item.ReturnedModel != RequiredModel ||
                    item.ReconciliationError is not null) ||
                item.Completed && !item.ActualUsageReliable && string.IsNullOrWhiteSpace(item.ReconciliationError) ||
                !item.Completed && (item.ActualUsd is not null || item.ReturnedModel is not null ||
                    item.HttpStatus is not null || item.Outcome is not null || item.ReconciliationError is not null)))
            throw new ServiceAcceptanceBudgetException("The existing acceptance ledger is invalid and cannot be reset.");
    }
}

public sealed class ServiceAcceptanceBudgetHandler(ServiceAcceptanceBudget budget) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        DateTime startedAt = DateTime.UtcNow;
        ValidateTarget(request);
        byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        int maximumOutputTokens = ValidateBody(body);
        ServiceAcceptanceReservation reservation = await budget.ReserveAsync(
            body.Length, maximumOutputTokens, ServiceAcceptanceBudget.RequiredModel, startedAt);
        HttpResponseMessage? response = null;
        GeminiUsageCompletion? completion = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, cancellationToken);
            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(cancellationToken));
                completion = GeminiUsagePricing.Parse(document.RootElement,
                    ServiceAcceptanceBudget.RequiredModel, startedAt, (int)response.StatusCode,
                    response.IsSuccessStatusCode ? "Observed" : "Rejected");
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
        const string path = "/v1beta/models/gemini-3.8-flash:generateContent";
        if (request.Method != HttpMethod.Post || request.RequestUri is null ||
            request.RequestUri.Scheme != Uri.UriSchemeHttps || !request.RequestUri.IsDefaultPort ||
            !string.IsNullOrEmpty(request.RequestUri.UserInfo) || request.RequestUri.Query.Length != 0 ||
            !string.Equals(request.RequestUri.Host, "generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
            request.RequestUri.AbsolutePath != path || request.Content is null)
            throw new ServiceAcceptanceBudgetException("Only the exact Gemini 3.8 Flash generateContent target is allowed.");
    }

    private static int ValidateBody(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement generation = document.RootElement.GetProperty("generationConfig");
            int maximum = generation.GetProperty("maxOutputTokens").GetInt32();
            string? thinking = generation.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString();
            if (maximum is not (8192 or 16384) || thinking != "high")
                throw new ServiceAcceptanceBudgetException("Serialized Gemini bounds or thinking level are not released values.");
            return maximum;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException)
        {
            throw new ServiceAcceptanceBudgetException("Serialized Gemini settings could not be verified.");
        }
    }
}

public sealed class ServiceAcceptanceBudgetException(string message) : HttpRequestException(message);
public sealed record ServiceAcceptanceReservation(int Ordinal, decimal ReservedUsd);
public sealed record ServiceAcceptanceCall(int Ordinal, string Phase, DateTime StartedAt, string RequestedModel,
    int BodyBytes, int MaximumOutputTokens, decimal ReservedUsd, decimal? ActualUsd,
    string PricingVersion, string? ReturnedModel, int? HttpStatus, string? Outcome,
    bool ActualUsageReliable, string? ReconciliationError, bool Completed);
public sealed record ServiceAcceptanceBudgetSnapshot(int MaximumCalls, decimal MaximumSpendUsd,
    int Calls, decimal CommittedSpendUsd, int UnknownUsageCalls, bool DispatchStopped,
    IReadOnlyList<ServiceAcceptanceCall> Items)
{
    public decimal KnownActualSpendUsd => Items.Where(value => value.Completed && value.ActualUsageReliable)
        .Sum(value => value.ActualUsd ?? 0m);
}
