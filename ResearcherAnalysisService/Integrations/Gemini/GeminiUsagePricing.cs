using System.Text.Json;

namespace ResearcherAnalysisService.Integrations.Gemini;

public static class GeminiUsagePricing
{
    private static readonly DateTime EarliestSupportedDate = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PriceChangeDate = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static GeminiUsageCompletion Parse(JsonElement root, string requestedModel, DateTime startedAt,
        int httpStatus, string outcome)
    {
        string? returnedModel = Text(root, "modelVersion");
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("usageMetadata", out JsonElement usage) ||
            usage.ValueKind != JsonValueKind.Object)
            return new() { Outcome = outcome, HttpStatus = httpStatus, ReturnedModel = returnedModel };

        bool valid = TryRequiredCount(usage, "promptTokenCount", out long? prompt) &
            TryRequiredCount(usage, "candidatesTokenCount", out long? candidates);
        valid &= TryOptionalCount(usage, "cachedContentTokenCount", 0, out long? cached);
        valid &= TryOptionalCount(usage, "thoughtsTokenCount", 0, out long? thoughts);
        valid &= TryOptionalCount(usage, "totalTokenCount", null, out long? total);
        valid &= TryOptionalCount(usage, "toolUsePromptTokenCount", 0, out long? toolUse);

        string? pricingVersion = null;
        decimal? estimate = null;
        string pricedModel = returnedModel ?? requestedModel;
        if (valid && prompt.HasValue && candidates.HasValue && cached.HasValue && thoughts.HasValue &&
            toolUse == 0 && cached <= prompt &&
            TrySum(prompt.Value, candidates.Value, thoughts.Value, out long calculated) &&
            (!total.HasValue || total.Value == calculated) &&
            TryGetRates(pricedModel, startedAt, out decimal inputRate, out decimal cachedRate,
                out decimal outputRate, out pricingVersion))
        {
            decimal uncachedTokens = prompt.Value - cached.Value;
            decimal outputTokens = candidates.Value + thoughts.Value;
            decimal calculatedEstimate = decimal.Round((uncachedTokens * inputRate + cached.Value * cachedRate +
                outputTokens * outputRate) / 1_000_000m, 9, MidpointRounding.AwayFromZero);
            if (calculatedEstimate <= 9_999_999_999.999999999m)
                estimate = calculatedEstimate;
        }

        return new()
        {
            Outcome = outcome, HttpStatus = httpStatus, ReturnedModel = returnedModel,
            PromptTokenCount = prompt, CachedTokenCount = cached, CandidateTokenCount = candidates,
            ThoughtTokenCount = thoughts, TotalTokenCount = total,
            PricingVersion = estimate.HasValue ? pricingVersion : null, EstimatedUsd = estimate
        };
    }

    private static bool TryGetRates(string model, DateTime startedAt, out decimal input,
        out decimal cached, out decimal output, out string? version)
    {
        input = cached = output = 0;
        version = null;
        if (!string.Equals(model, "gemini-3.8-flash", StringComparison.OrdinalIgnoreCase) ||
            startedAt.Kind != DateTimeKind.Utc || startedAt < EarliestSupportedDate)
            return false;
        if (startedAt < PriceChangeDate)
        {
            input = 0.75m;
            cached = 0.075m;
            output = 3.75m;
            version = "gemini-3.8-flash-standard-through-2026-12-31";
        }
        else
        {
            input = 1.50m;
            cached = 0.15m;
            output = 7.50m;
            version = "gemini-3.8-flash-standard-from-2027-01-01";
        }
        return true;
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool TryRequiredCount(JsonElement root, string name, out long? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out long parsed) || parsed < 0)
            return false;
        value = parsed;
        return true;
    }

    private static bool TryOptionalCount(JsonElement root, string name, long? missingValue, out long? value)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            value = missingValue;
            return true;
        }
        value = null;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out long parsed) || parsed < 0)
            return false;
        value = parsed;
        return true;
    }

    private static bool TrySum(long prompt, long candidates, long thoughts, out long total)
    {
        try
        {
            total = checked(prompt + candidates + thoughts);
            return true;
        }
        catch (OverflowException)
        {
            total = 0;
            return false;
        }
    }
}
