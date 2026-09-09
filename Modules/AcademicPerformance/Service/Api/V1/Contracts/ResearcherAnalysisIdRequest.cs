using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ResearcherAnalysisIdRequest : IValidatableObject
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    public DateTimeOffset? SnapshotAt { get; set; } = null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SnapshotAt is { } snapshotAt &&
            (snapshotAt == default || snapshotAt > DateTimeOffset.UtcNow.AddMinutes(5)))
            yield return new ValidationResult("Supply a valid snapshot time, not a future date.", [nameof(SnapshotAt)]);
    }
}
