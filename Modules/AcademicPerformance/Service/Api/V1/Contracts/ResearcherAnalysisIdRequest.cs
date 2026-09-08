using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ResearcherAnalysisIdRequest : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int? ResearcherId { get; set; } = null;

    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string? PersonelId { get; set; } = null;

    public DateTimeOffset? SnapshotAt { get; set; } = null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ResearcherId is null && PersonelId is null)
            yield return new ValidationResult("PersonelID or ResearcherId must be supplied.",
                [nameof(PersonelId), nameof(ResearcherId)]);
        if (PersonelId is not null && string.IsNullOrWhiteSpace(PersonelId))
            yield return new ValidationResult("PersonelID cannot be empty.", [nameof(PersonelId)]);
        if (PersonelId?.Trim().Length > 200)
            yield return new ValidationResult("PersonelID must be at most 200 characters.", [nameof(PersonelId)]);
        if (SnapshotAt is { } snapshotAt &&
            (snapshotAt == default || snapshotAt > DateTimeOffset.UtcNow.AddMinutes(5)))
            yield return new ValidationResult("Supply a valid snapshot time, not a future date.", [nameof(SnapshotAt)]);
    }
}
