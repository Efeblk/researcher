using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed class CanonicalArticleReviewRequest : IValidatableObject
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CanonicalWorkId { get; set; }

    public string Language { get; set; } = "tr";
    public bool ForceRegeneration { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Language is not ("tr" or "en"))
            yield return new("Language must be tr or en.", [nameof(Language)]);
    }
}

public sealed class CanonicalArticleReviewResponse
{
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public int CanonicalWorkId { get; set; }
    public long ReviewRunId { get; set; }
    public long BaseAnalysisRunId { get; set; }
    public DateTimeOffset ReviewedAt { get; set; }
    public bool Reused { get; set; }
    public bool IsStale { get; set; }
    public List<string> StaleReasons { get; set; } = [];
    public ArticleReviewReport Report { get; set; } = null!;
}
