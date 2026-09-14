using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Serialization;

namespace AcademicCollector.Analysis.Contracts;

public static class FacultyAssistantAnalysisLimits
{
    public const int MaximumEvidenceCount = 20;
    public const int MaximumEvidenceTextCharacters = 12000;
    public const int MaximumPrivateContextCharacters = 12000;
    public const int MaximumInputUtf8Bytes = 100000;
}

public sealed class FacultyAssistantAnalysisRequest : IValidatableObject
{
    [Required, RegularExpression("^(OwnPaperMethods|OwnPaperIssues|TeachingHelp|RelatedWorks|ExploreOwnRecord)$")]
    public string Mode { get; set; } = string.Empty;
    [Required, RegularExpression("^(tr|en)$")] public string Language { get; set; } = "tr";
    [Required, StringLength(2000)] public string Query { get; set; } = string.Empty;
    [StringLength(FacultyAssistantAnalysisLimits.MaximumPrivateContextCharacters)]
    public string? PrivateContext { get; set; }
    [Required, MinLength(1), MaxLength(FacultyAssistantAnalysisLimits.MaximumEvidenceCount)]
    public List<FacultyAssistantEvidence> Evidence { get; set; } = [];
    [Required, StringLength(64)] public string EvidenceCatalogHash { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Evidence is null || Evidence.Any(value => value is null))
        {
            yield return new("Evidence cannot contain null entries.", [nameof(Evidence)]);
            yield break;
        }
        if (Evidence.Any(value => string.IsNullOrWhiteSpace(value.EvidenceId) || value.EvidenceId.Length > 300 ||
            value.CanonicalWorkId <= 0 || value.SourceSpanId <= 0 || string.IsNullOrWhiteSpace(value.SourceId) ||
            value.SourceId.Length > 300 || value.StartOffset < 0 || value.EndOffset < value.StartOffset ||
            value.EndOffset <= value.StartOffset || string.IsNullOrWhiteSpace(value.ExactText) ||
            value.ExactText.Length > FacultyAssistantAnalysisLimits.MaximumEvidenceTextCharacters ||
            value.EndOffset - value.StartOffset != value.ExactText.Length ||
            string.IsNullOrWhiteSpace(value.SourceKind) || value.SourceKind.Length > 20))
            yield return new("Evidence entries are invalid or exceed their bounds.", [nameof(Evidence)]);
        if (Evidence.Select(value => value.EvidenceId).Distinct(StringComparer.Ordinal).Count() != Evidence.Count)
            yield return new("Evidence IDs must be unique.", [nameof(Evidence)]);
        if (Encoding.UTF8.GetByteCount(Query + PrivateContext + string.Concat(Evidence.Select(value => value.ExactText))) >
            FacultyAssistantAnalysisLimits.MaximumInputUtf8Bytes)
            yield return new($"The complete assistant input exceeds {FacultyAssistantAnalysisLimits.MaximumInputUtf8Bytes:N0} UTF-8 bytes.", [nameof(Evidence)]);
    }
}

public sealed record FacultyAssistantEvidence(
    string EvidenceId, int CanonicalWorkId, long SourceSpanId, string SourceId,
    int? PageNumber, int StartOffset, int EndOffset, string ExactText,
    string SourceKind, bool IsPartial);

public sealed record FacultyAssistantCitation(string EvidenceId, string ExactQuote);
public sealed record FacultyAssistantAnswerItem(string Kind, string Basis, string? Response,
    IReadOnlyList<FacultyAssistantCitation> Citations)
{
    public string? CandidateId { get; init; }
}
public sealed record FacultyAssistantVerification(string Status, string Model,
    string PromptVersion, bool UsesSameModelFamily, string Limitation);
public sealed record FacultyAssistantCoverage(
    [property: JsonRequired] int CandidateItems,
    [property: JsonRequired] int AutomaticallyCheckedItems,
    [property: JsonRequired] int SupportedItems,
    [property: JsonRequired] int UnsupportedItems,
    [property: JsonRequired] int UncertainItems,
    [property: JsonRequired] int OmittedItems,
    [property: JsonRequired] bool IsPartial)
{
    public int UnverifiedItems { get; init; }
    public int RepairCandidateItems { get; init; }
}
public sealed record FacultyAssistantSourceCheck(
    [property: JsonRequired] Guid AttemptId,
    [property: JsonRequired] string CandidateId,
    [property: JsonRequired] string Origin,
    [property: JsonRequired] string Status,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] string Model,
    [property: JsonRequired] string PromptVersion,
    [property: JsonRequired] IReadOnlyList<string> EvidenceIds);
public sealed record FacultyAssistantSourceChecks(
    [property: JsonRequired] int TotalCandidates,
    [property: JsonRequired] int CompletedChecks,
    [property: JsonRequired] int SupportedItems,
    [property: JsonRequired] int UnsupportedItems,
    [property: JsonRequired] int UncertainItems,
    [property: JsonRequired] int UnverifiedOutputLimitItems,
    [property: JsonRequired] int UnverifiedInvalidResponseItems,
    [property: JsonRequired] IReadOnlyList<FacultyAssistantSourceCheck> Checks);
public sealed record FacultyRequestCoverageRequirement(
    [property: JsonRequired] string RequirementId,
    [property: JsonRequired] string Requirement,
    [property: JsonRequired] string Status,
    // One-based indexes into FacultyAssistantAnalysisReport.Items.
    [property: JsonRequired] IReadOnlyList<int> ItemIndexes,
    [property: JsonRequired] string Reason);
public sealed record FacultyRequestCoverage(
    [property: JsonRequired] string Status,
    [property: JsonRequired] IReadOnlyList<FacultyRequestCoverageRequirement> Requirements,
    [property: JsonRequired] string Model,
    [property: JsonRequired] string PromptVersion,
    [property: JsonRequired] bool UsesSameModelFamily,
    [property: JsonRequired] string Limitation);
public sealed record FacultyAssistantGenerationAttempt(
    [property: JsonRequired] int Ordinal,
    [property: JsonRequired] string ThinkingLevel,
    [property: JsonRequired] string Outcome,
    [property: JsonRequired] string Model,
    [property: JsonRequired] long TotalTokenCount,
    [property: JsonRequired] decimal EstimatedUsd,
    [property: JsonRequired] string PricingVersion,
    [property: JsonRequired] bool UsagePersisted)
{
    public Guid AttemptId { get; init; }
}
public sealed record FacultyAssistantGeneration(
    [property: JsonRequired] IReadOnlyList<FacultyAssistantGenerationAttempt> Attempts,
    [property: JsonRequired] bool UsedOutputLimitRecovery);
public sealed record FacultyAssistantRepair(
    [property: JsonRequired] string Status,
    [property: JsonRequired] int RequestedCandidates,
    [property: JsonRequired] int GeneratedCandidates,
    [property: JsonRequired] int RetainedItems,
    [property: JsonRequired] string Model,
    [property: JsonRequired] string PromptVersion,
    FacultyAssistantGenerationAttempt? Attempt);
public sealed record FacultyAssistantAnalysisReport(string Mode, string Language,
    IReadOnlyList<FacultyAssistantAnswerItem> Items, string Model, string PromptVersion,
    FacultyAssistantVerification Verification, string Outcome = "completed",
    FacultyAssistantCoverage? Coverage = null)
{
    public FacultyRequestCoverage? RequestCoverage { get; init; }
    public FacultyAssistantGeneration? Generation { get; init; }
    public FacultyAssistantSourceChecks? SourceChecks { get; init; }
    public FacultyAssistantRepair? Repair { get; init; }
}
