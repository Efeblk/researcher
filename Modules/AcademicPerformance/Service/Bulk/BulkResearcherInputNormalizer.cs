using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;

public sealed class BulkResearcherInputNormalizer(ResearcherProviderInputNormalizer normalizer)
{
    public const int MaximumProviderInputLength = ResearcherProviderInputNormalizer.MaximumProviderInputLength;

    public BulkNormalizationResult Normalize(BulkResearcherInput input)
    {
        ResearcherProviderInputNormalizationResult result = normalizer.Normalize(new()
        {
            Orcid = input.Orcid,
            GoogleScholarId = input.GoogleScholarId,
            WebOfScienceResearcherId = input.WebOfScienceId,
            ScopusId = input.ScopusId
        });
        return new(new()
        {
            PersonelId = input.PersonelId.Trim(),
            Orcid = result.Input.Orcid,
            GoogleScholarId = result.Input.GoogleScholarId,
            WebOfScienceId = result.Input.WebOfScienceResearcherId,
            ScopusId = input.ScopusId?.Trim()
        }, result.Warnings, result.RejectionReason);
    }
}

public sealed record BulkNormalizationResult(
    BulkResearcherInput Input, List<string> Warnings, string? RejectionReason);
