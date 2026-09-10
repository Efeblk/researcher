using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ArticleSummaryRequest : IValidatableObject
{
    public string PersonelID { get; set; } = string.Empty;
    public int AcademicWorkId { get; set; }
    public string Language { get; set; } = "tr";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(PersonelID)) yield return new("PersonelID is required.", [nameof(PersonelID)]);
        if (AcademicWorkId <= 0) yield return new("AcademicWorkId must be positive.", [nameof(AcademicWorkId)]);
        if (Language is not ("tr" or "en")) yield return new("Language must be tr or en.", [nameof(Language)]);
    }
}
