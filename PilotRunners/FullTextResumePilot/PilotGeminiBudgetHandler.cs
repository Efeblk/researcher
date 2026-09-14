using System.Net;
using System.Text.Json;
using ResearcherAnalysisService.Integrations.Gemini;

namespace FullTextResumePilot;

public sealed class PilotGeminiBudgetState(int maximumCalls = 32, decimal maximumSpendUsd = 1.00m)
{
    private readonly object gate = new();
    private readonly List<PilotGeminiCall> calls = [];
    private decimal committedSpendUsd;
    private bool dispatchStopped;

    public int MaximumCalls { get; } = maximumCalls;
    public decimal MaximumSpendUsd { get; } = maximumSpendUsd;

    public static PilotGeminiBudgetState Restore(PilotGeminiBudgetSnapshot snapshot)
    {
        if (snapshot.MaximumCalls <= 0 || snapshot.MaximumSpendUsd <= 0 ||
            snapshot.Calls != snapshot.Items.Count || snapshot.Calls > snapshot.MaximumCalls ||
            snapshot.CommittedSpendUsd < 0 || snapshot.CommittedSpendUsd > snapshot.MaximumSpendUsd ||
            snapshot.Items.Select((value, index) => value.Ordinal == index + 1).Any(value => !value))
            throw new InvalidOperationException("The persisted pilot budget snapshot is invalid.");

        PilotGeminiBudgetState state = new(snapshot.MaximumCalls, snapshot.MaximumSpendUsd);
        lock (state.gate)
        {
            state.calls.AddRange(snapshot.Items);
            state.committedSpendUsd = snapshot.CommittedSpendUsd;
            state.dispatchStopped = snapshot.DispatchStopped;
        }
        return state;
    }

    public static void ValidateResumeSnapshot(PilotGeminiBudgetSnapshot snapshot, int sqlCalls,
        decimal? sqlSpendUsd, bool sqlHasIncompleteOrUnknownAttempts)
    {
        if (snapshot.MaximumCalls != 64 || snapshot.MaximumSpendUsd != 2m)
            throw new InvalidOperationException("The persisted pilot budget limits do not match the acceptance run.");
        if (snapshot.Calls != snapshot.Items.Count || snapshot.Items.Any(value =>
                !value.Completed || !value.ActualUsageReliable || value.ActualUsd is null ||
                value.ReconciliationError is not null))
            throw new InvalidOperationException("The persisted pilot ledger contains a missing, pending, or unknown call.");
        decimal recomputed = snapshot.Items.Sum(value => value.ActualUsd!.Value);
        if (recomputed != snapshot.CommittedSpendUsd || sqlHasIncompleteOrUnknownAttempts ||
            sqlCalls != snapshot.Calls || sqlSpendUsd is null || sqlSpendUsd.Value != recomputed)
            throw new InvalidOperationException("The persisted pilot ledger does not agree with its items and SQL usage.");
    }

    public PilotGeminiReservation Reserve(int bodyBytes, int maximumOutputTokens, string model, DateTime startedAt)
    {
        if (!GeminiUsagePricing.TryGetRates(model, startedAt, out decimal inputRate, out _,
                out decimal outputRate, out string? pricingVersion))
            throw new PilotBudgetException("Gemini pricing is unavailable for the requested model and UTC date.");

        decimal raw = ((bodyBytes + 512m) * inputRate + maximumOutputTokens * outputRate) / 1_000_000m;
        decimal amount = decimal.Ceiling(raw * 1_000_000_000m) / 1_000_000_000m;
        lock (gate)
        {
            if (dispatchStopped)
                throw new PilotBudgetException("The pilot budget guard stopped dispatch after an invalid usage reconciliation.");
            if (calls.Count >= MaximumCalls)
                throw new PilotBudgetException($"The pilot call ceiling of {MaximumCalls} was reached.");
            if (committedSpendUsd + amount > MaximumSpendUsd)
                throw new PilotBudgetException($"The next conservative reservation would exceed the pilot USD {MaximumSpendUsd:F2} ceiling.");

            int ordinal = calls.Count + 1;
            committedSpendUsd += amount;
            calls.Add(new(ordinal, startedAt, model, bodyBytes, maximumOutputTokens, amount,
                null, pricingVersion!, null, null, false));
            return new(ordinal, amount);
        }
    }

    public void Complete(int ordinal, HttpStatusCode? statusCode, GeminiUsageCompletion? completion)
    {
        lock (gate)
        {
            int index = ordinal - 1;
            PilotGeminiCall current = calls[index];
            if (current.Completed)
                return;
            decimal? actual = completion?.EstimatedUsd;
            bool matchingMetadata = completion is { UsageValidForAttribution: true, EstimatedUsd: not null } &&
                completion.EstimatedUsd >= 0 &&
                string.Equals(completion.PricingVersion, current.PricingVersion, StringComparison.Ordinal) &&
                string.Equals(completion.ReturnedModel, current.Model, StringComparison.OrdinalIgnoreCase);
            bool reliable = matchingMetadata && actual <= current.ReservedUsd;
            string? reconciliationError = null;
            if (reliable)
                committedSpendUsd += actual!.Value - current.ReservedUsd;
            else if (matchingMetadata && actual > current.ReservedUsd)
            {
                committedSpendUsd += actual.Value - current.ReservedUsd;
                dispatchStopped = true;
                reconciliationError = "actual_usage_exceeded_reservation";
            }
            else if (completion is not null && completion.UsageValidForAttribution)
                reconciliationError = "usage_pricing_or_model_mismatch";
            calls[index] = current with
            {
                ActualUsd = actual,
                HttpStatus = statusCode is null ? null : (int)statusCode.Value,
                Outcome = completion?.Outcome,
                ActualUsageReliable = reliable,
                ReconciliationError = reconciliationError,
                Completed = true
            };
        }
    }

    public PilotGeminiBudgetSnapshot Snapshot()
    {
        lock (gate)
            return new(MaximumCalls, MaximumSpendUsd, calls.Count, committedSpendUsd,
                calls.Count(value => !value.ActualUsageReliable), dispatchStopped, calls.ToArray());
    }
}

public sealed class PilotGeminiBudgetHandler(PilotGeminiBudgetState budget) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        DateTime startedAt = DateTime.UtcNow;
        if (request.Method != HttpMethod.Post || request.RequestUri is null ||
            request.RequestUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(request.RequestUri.Host, "generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
            !request.RequestUri.IsDefaultPort || !string.IsNullOrEmpty(request.RequestUri.UserInfo) ||
            !request.RequestUri.AbsolutePath.EndsWith(":generateContent", StringComparison.Ordinal))
            throw new PilotBudgetException("The live pilot permits only Gemini generateContent POST requests.");
        string model = ParseModel(request.RequestUri);
        if (request.Content is null)
            throw new PilotBudgetException("The Gemini request has no serialized body.");
        byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        int maximumOutputTokens = ParseMaximumOutputTokens(body);
        PilotGeminiReservation reservation = budget.Reserve(body.Length, maximumOutputTokens, model, startedAt);

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
                completion = GeminiUsagePricing.Parse(document.RootElement, model, startedAt,
                    (int)response.StatusCode, response.IsSuccessStatusCode ? "Observed" : "Rejected");
            }
            catch (JsonException)
            {
                // Unknown usage intentionally retains the full conservative reservation.
            }
            return response;
        }
        finally
        {
            budget.Complete(reservation.Ordinal, response?.StatusCode, completion);
        }
    }

    private static string ParseModel(Uri requestUri)
    {
        const string prefix = "/v1beta/models/";
        string path = requestUri.AbsolutePath;
        int start = path.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0 || !path.EndsWith(":generateContent", StringComparison.Ordinal))
            throw new PilotBudgetException("The Gemini model could not be derived from the request URI.");
        start += prefix.Length;
        string escaped = path[start..^":generateContent".Length];
        string model = Uri.UnescapeDataString(escaped);
        if (string.IsNullOrWhiteSpace(model) || model.Contains('/'))
            throw new PilotBudgetException("The Gemini model in the request URI is invalid.");
        return model;
    }

    private static int ParseMaximumOutputTokens(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            int value = document.RootElement.GetProperty("generationConfig")
                .GetProperty("maxOutputTokens").GetInt32();
            if (value <= 0)
                throw new PilotBudgetException("Gemini maxOutputTokens must be positive.");
            return value;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException)
        {
            throw new PilotBudgetException("Gemini maxOutputTokens could not be derived from the serialized request body.");
        }
    }
}

public sealed class PilotBudgetException(string message) : HttpRequestException(message);

public sealed record PilotGeminiReservation(int Ordinal, decimal ReservedUsd);

public sealed record PilotGeminiCall(
    int Ordinal,
    DateTime StartedAt,
    string Model,
    int BodyBytes,
    int MaximumOutputTokens,
    decimal ReservedUsd,
    decimal? ActualUsd,
    string PricingVersion,
    int? HttpStatus,
    string? Outcome,
    bool ActualUsageReliable,
    string? ReconciliationError = null,
    bool Completed = false);

public sealed record PilotGeminiBudgetSnapshot(
    int MaximumCalls,
    decimal MaximumSpendUsd,
    int Calls,
    decimal CommittedSpendUsd,
    int UnknownUsageCalls,
    bool DispatchStopped,
    IReadOnlyList<PilotGeminiCall> Items);
