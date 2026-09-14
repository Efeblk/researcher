using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public interface IFacultyAssistantGenerator
{
    Task<GeneratedFacultyAssistantAnswer> GenerateAsync(
        FacultyAssistantAnalysisRequest request, CancellationToken cancellationToken);
}

public interface IFacultyAssistantVerifier
{
    Task<GeneratedFacultyAssistantSourceCheck> VerifyAsync(
        string role, string language, GeneratedArticleReviewFinding finding,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken);
}

public interface IFacultyAssistantRepairGenerator
{
    Task<GeneratedFacultyAssistantRepair> RepairAsync(
        FacultyAssistantAnalysisRequest request,
        IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> omittedCandidates,
        IReadOnlyList<GeneratedFacultyAssistantItem> supportedItems,
        CancellationToken cancellationToken);
}

public sealed record GeneratedFacultyAssistantItem(string Kind, string Basis, string? Response,
    IReadOnlyList<string> EvidenceIds);
public sealed record GeneratedFacultyAssistantGenerationAttempt(int Ordinal, string ThinkingLevel,
    string Outcome, string Model, long TotalTokenCount, decimal EstimatedUsd, string PricingVersion,
    bool UsagePersisted)
{
    public Guid AttemptId { get; init; }
}
public sealed record GeneratedFacultyAssistantAnswer(IReadOnlyList<GeneratedFacultyAssistantItem> Items,
    string Model, string PromptVersion,
    IReadOnlyList<GeneratedFacultyAssistantGenerationAttempt> GenerationAttempts);
public sealed record GeneratedFacultyAssistantSourceCheck(Guid AttemptId, string Status,
    GeneratedArticleReviewVerdict? Verdict, string Model, string PromptVersion, string Reason);
public sealed record GeneratedFacultyAssistantRepairCandidate(string CandidateId,
    GeneratedFacultyAssistantItem Item, string Verdict, string Reason);
public sealed record GeneratedFacultyAssistantRepairItem(string CandidateId,
    GeneratedFacultyAssistantItem Item);
public sealed record GeneratedFacultyAssistantRepair(string Status,
    IReadOnlyList<GeneratedFacultyAssistantRepairItem> Items, string Model, string PromptVersion,
    GeneratedFacultyAssistantGenerationAttempt Attempt);
