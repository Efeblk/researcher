using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AcademicCollector.Analysis.Contracts;

public sealed class AnalyzeResearcherRequest : IValidatableObject
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string ResearcherName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Department { get; set; } = null;

    public DateTimeOffset SnapshotAt { get; set; }

    [Required, RegularExpression("^(en|tr)$")]
    public string Language { get; set; } = "en";

    [Range(0, int.MaxValue)]
    public int TotalPublicationCount { get; set; }

    [Required, MinLength(1), MaxLength(100)]
    public List<AnalysisPublication> Publications { get; set; } = [];

    [Required, MaxLength(10)]
    public List<ProviderMetrics> CitationMetrics { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SnapshotAt == default || SnapshotAt > DateTimeOffset.UtcNow.AddMinutes(5))
            yield return new ValidationResult("Supply the snapshot's UTC collection time.", [nameof(SnapshotAt)]);

        if (Publications is not null)
        {
            if (TotalPublicationCount < Publications.Count)
                yield return new ValidationResult("Total count must include all submitted publications.", [nameof(TotalPublicationCount)]);
            if (Publications.Any(publication => publication is null))
                yield return new ValidationResult("Publications cannot contain null entries.", [nameof(Publications)]);
            else
            {
                if (Publications.Select(publication => publication.Id).Distinct(StringComparer.Ordinal).Count() != Publications.Count)
                    yield return new ValidationResult("Publication IDs must be unique within the snapshot.", [nameof(Publications)]);
                int characters = Publications.Sum(publication =>
                    (publication.Title?.Length ?? 0) + (publication.Abstract?.Length ?? 0) +
                    (publication.Keywords?.Length ?? 0));
                if (characters > 60000)
                    yield return new ValidationResult("Submit at most 60,000 characters of publication text; select a smaller sample.", [nameof(Publications)]);
            }
        }

        if (CitationMetrics?.Any(metric => metric is null) == true)
            yield return new ValidationResult("Citation metrics cannot contain null entries.", [nameof(CitationMetrics)]);
    }
}
