using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public interface IFacultyRequestCoverageVerifier
{
    Task<GeneratedFacultyRequestCoverage> VerifyAsync(
        string mode, string language, string query,
        IReadOnlyList<FacultyAssistantAnswerItem> retainedItems,
        CancellationToken cancellationToken);
}

public sealed record GeneratedFacultyRequestCoverage(
    string Status,
    IReadOnlyList<GeneratedFacultyRequestCoverageRequirement> Requirements,
    string Model,
    string PromptVersion);

public sealed record GeneratedFacultyRequestCoverageRequirement(
    string RequirementId,
    string Requirement,
    string Status,
    IReadOnlyList<int> ItemIndexes,
    string Reason);
