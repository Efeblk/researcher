using System.ComponentModel.DataAnnotations;

namespace ResearcherAnalysisService.Products.Evaluations;

public sealed class ArticleEvaluationOptions
{
    public bool WorkerEnabled { get; set; } = true;

    [Range(1, 60)]
    public int PollSeconds { get; set; } = 5;

    [Range(10, 1800)]
    public int RequestTimeoutSeconds { get; set; } = 330;

    [Range(1024, 1048576)]
    public int MaximumSourceBytes { get; set; } = 100000;

    [Required, StringLength(100, MinimumLength = 1)]
    public string EvaluatorVersion { get; set; } = "article-evaluator-v1";

    [Required, StringLength(100, MinimumLength = 1)]
    public string PolicyVersion { get; set; } = "article-evaluation-policy-v1";
}
