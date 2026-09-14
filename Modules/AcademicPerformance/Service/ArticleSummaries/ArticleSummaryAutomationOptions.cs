using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSummaryAutomationOptions
{
    public bool Enabled { get; set; } = true;
    public bool WorkerEnabled { get; set; } = true;

    [RegularExpression("^(en|tr)$")]
    public string Language { get; set; } = "tr";

    [Range(1, 60)]
    public int PollSeconds { get; set; } = 5;

    [Range(1, 3600)]
    public int RetrySeconds { get; set; } = 60;

    [Range(1, 10)]
    public int MaximumAttempts { get; set; } = 3;

    [Required, StringLength(100, MinimumLength = 1)]
    public string PolicyVersion { get; set; } = "article-summary-v5";
}
