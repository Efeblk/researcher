using System.ComponentModel.DataAnnotations;

namespace ResearcherAnalysisService.Products.Analysis;

public sealed class AnalysisServiceOptions
{
    [RegularExpression("^(en|tr)$")]
    public string Language { get; set; } = "en";
    [Range(1, 100)]
    public int MaximumPublications { get; set; } = 10;
    [Range(200, 3000)]
    public int MaximumTextBytes { get; set; } = 2500;
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 240;
}
