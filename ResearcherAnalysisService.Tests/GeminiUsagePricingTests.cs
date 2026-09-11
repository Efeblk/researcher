using System.Text.Json;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ResearcherAnalysisService.Tests;

public sealed class GeminiUsagePricingTests
{
    [Theory]
    [InlineData("2026-12-31T23:59:59Z", 0.001065, "through-2026-12-31")]
    [InlineData("2027-01-01T00:00:00Z", 0.002130, "from-2027-01-01")]
    public void Parse_SupportedPaidStandardUsage_CalculatesVersionedEstimate(
        string started, decimal expected, string versionSuffix)
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"modelVersion":"gemini-3.8-flash","usageMetadata":{"promptTokenCount":1000,
             "cachedContentTokenCount":200,"candidatesTokenCount":100,"thoughtsTokenCount":20,
             "totalTokenCount":1120}}
            """);

        GeminiUsageCompletion result = GeminiUsagePricing.Parse(document.RootElement,
            "gemini-3.8-flash", DateTime.Parse(started).ToUniversalTime(), 200, "Success");

        Assert.Equal(expected, result.EstimatedUsd);
        Assert.EndsWith(versionSuffix, result.PricingVersion);
        Assert.Equal(200, result.CachedTokenCount);
        Assert.Equal(20, result.ThoughtTokenCount);
    }

    [Fact]
    public void Parse_OmittedCachedAndThoughtCounts_TreatsThemAsZero()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"modelVersion":"gemini-3.8-flash","usageMetadata":{"promptTokenCount":1000,
             "candidatesTokenCount":100,"totalTokenCount":1100}}
            """);

        GeminiUsageCompletion result = GeminiUsagePricing.Parse(document.RootElement,
            "gemini-3.8-flash", new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc), 200, "Success");

        Assert.Equal(0, result.CachedTokenCount);
        Assert.Equal(0, result.ThoughtTokenCount);
        Assert.Equal(0.001125m, result.EstimatedUsd);
    }

    [Theory]
    [InlineData("{\"usageMetadata\":{\"promptTokenCount\":10,\"cachedContentTokenCount\":11,\"candidatesTokenCount\":1}}", "gemini-3.8-flash", "2026-09-03T00:00:00Z")]
    [InlineData("{\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":1,\"totalTokenCount\":99}}", "gemini-3.8-flash", "2026-09-03T00:00:00Z")]
    [InlineData("{\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":1,\"toolUsePromptTokenCount\":1}}", "gemini-3.8-flash", "2026-09-03T00:00:00Z")]
    [InlineData("{\"usageMetadata\":{\"promptTokenCount\":10}}", "gemini-3.8-flash", "2026-09-03T00:00:00Z")]
    [InlineData("{\"usageMetadata\":{\"promptTokenCount\":\"10\",\"candidatesTokenCount\":1}}", "gemini-3.8-flash", "2026-09-03T00:00:00Z")]
    [InlineData("{\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":1}}", "gemini-other", "2026-09-03T00:00:00Z")]
    [InlineData("{\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":1}}", "gemini-3.8-flash", "2026-09-02T23:59:59Z")]
    [InlineData("[]", "gemini-3.8-flash", "2026-09-03T00:00:00Z")]
    public void Parse_UnsupportedOrIncoherentUsage_LeavesEstimateUnknown(
        string json, string model, string started)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        GeminiUsageCompletion result = GeminiUsagePricing.Parse(document.RootElement, model,
            DateTime.Parse(started).ToUniversalTime(), 200, "InvalidResponse");

        Assert.Null(result.EstimatedUsd);
        Assert.Null(result.PricingVersion);
    }

    [Fact]
    public void Parse_CountOverflowOrDatabaseDecimalOverflow_LeavesEstimateUnknown()
    {
        using JsonDocument countOverflow = JsonDocument.Parse(
            "{\"usageMetadata\":{\"promptTokenCount\":" + long.MaxValue +
            ",\"candidatesTokenCount\":1}}");
        using JsonDocument decimalOverflow = JsonDocument.Parse("""
            {"usageMetadata":{"promptTokenCount":9000000000000000000,"candidatesTokenCount":0}}
            """);
        DateTime started = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Null(GeminiUsagePricing.Parse(countOverflow.RootElement, "gemini-3.8-flash",
            started, 200, "Success").EstimatedUsd);
        Assert.Null(GeminiUsagePricing.Parse(decimalOverflow.RootElement, "gemini-3.8-flash",
            started, 200, "Success").EstimatedUsd);
    }
}
