using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ResearcherAnalysisIdRequest : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int ResearcherId { get; set; }

    public DateTimeOffset? SnapshotAt { get; set; } = null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SnapshotAt is { } snapshotAt &&
            (snapshotAt == default || snapshotAt > DateTimeOffset.UtcNow.AddMinutes(5)))
            yield return new ValidationResult("Supply a valid snapshot time, not a future date.", [nameof(SnapshotAt)]);
    }
}
