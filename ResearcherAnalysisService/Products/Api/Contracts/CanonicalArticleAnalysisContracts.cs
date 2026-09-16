using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed class CanonicalArticleAnalysisRequest : IValidatableObject
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CanonicalWorkId { get; set; }

    public string Language { get; set; } = "tr";

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, 200)]
    public int Take { get; set; } = 100;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Language is not ("tr" or "en"))
            yield return new("Language must be tr or en.", [nameof(Language)]);
    }
}

public sealed record CanonicalArticleAnalysisResponse(
    ArticleSummaryAutomationStatusResponse Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] CanonicalArticleEvidenceResponse? Evidence,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] CanonicalArticleReviewResponse? Review);
