using System.ComponentModel.DataAnnotations;

namespace ResearcherAnalysisService.Products.ArticleReviews;

public sealed class ArticleReviewOptions
{
    [Required, StringLength(100)]
    public string PolicyVersion { get; set; } = "article-specialist-review-policy-v5";

    [Range(5, 600)]
    public int TotalTimeoutSeconds { get; set; } = 600;

    [Range(4096, 1000000)]
    public int MaximumSourceBytes { get; set; } = 100000;

    [Range(8, 64)]
    public int MaximumProviderCalls { get; set; } = 24;

    [Range(0.01, 100.0)]
    public decimal MaximumSpendUsd { get; set; } = 1.00m;
}
