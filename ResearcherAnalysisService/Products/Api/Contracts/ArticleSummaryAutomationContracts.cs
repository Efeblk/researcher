using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed class ArticleSummaryAutomationStatusRequest : IValidatableObject
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CanonicalWorkId { get; set; }

    public string Language { get; set; } = "tr";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Language is not ("tr" or "en"))
            yield return new("Language must be tr or en.", [nameof(Language)]);
    }
}

public sealed class ArticleSummaryAutomationStatusResponse
{
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public int CanonicalWorkId { get; set; }
    public string Language { get; set; } = string.Empty;
    public bool AutomationEnabled { get; set; }
    public bool WorkerEnabled { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; } = null;
    public DateTime? StartedAt { get; set; } = null;
    public DateTime? CompletedAt { get; set; } = null;
    public DateTime? UpdatedAt { get; set; } = null;
    public string? LastOutcomeCode { get; set; } = null;
    public string? LastOutcomeMessage { get; set; } = null;
    public ArticleSummaryAutomationSuccessDto? LastSuccess { get; set; } = null;
}

public sealed class ArticleSummaryAutomationSuccessDto
{
    public long AnalysisRunId { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    public string Language { get; set; } = string.Empty;
    public string? PolicyVersion { get; set; } = null;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
}
