using System.ComponentModel.DataAnnotations;

namespace ResearcherAnalysisService.Configuration;

public sealed class ArticleEvaluationOptions
{
    [Range(5, 600)]
    public int TimeoutSeconds { get; set; } = 300;

    [Range(4096, 1_000_000)]
    public int MaximumInputBytes { get; set; } = 100_000;

    [Range(4096, 393_216)]
    public int ContextTokens { get; set; } = 131_072;

    [Range(512, 16_000)]
    public int MaxOutputTokens { get; set; } = 8_192;

    [Range(512, 16_000)]
    public int VerifierMaxOutputTokens { get; set; } = 8_192;

    public DeepSeekEvaluationOptions DeepSeek { get; set; } = new();
}
public sealed class DeepSeekEvaluationOptions
{
    public string? ApiKey { get; set; } = null;
    public string? PricingVersion { get; set; } = null;
    public decimal? InputUsdPerMillionTokens { get; set; } = null;
    public decimal? CacheHitUsdPerMillionTokens { get; set; } = null;
    public decimal? OutputUsdPerMillionTokens { get; set; } = null;
}
