using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public interface IResearcherReportGenerator
{
    Task<GeneratedFindings> GenerateAsync(AnalyzeResearcherRequest request, CancellationToken cancellationToken);
}

public sealed record GeneratedFindings(AnalysisFindings Findings, string Model, string PromptVersion);
