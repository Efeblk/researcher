using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

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
        string? tcKimlikNo = null;
        string? rejectionReason = result.RejectionReason;
        if (!string.IsNullOrWhiteSpace(input.TcKimlikNo))
        {
            try
            {
                tcKimlikNo = YoksisCollectionService.ValidateTcKimlikNo(input.TcKimlikNo);
                if (result.Input.Orcid is null && result.Input.GoogleScholarId is null &&
                    result.Input.WebOfScienceResearcherId is null)
                    rejectionReason = null;
            }
            catch (ArgumentException)
            {
                rejectionReason = "T.C. kimlik numarası biçimi geçersiz.";
            }
        }
        return new(new()
        {
            PersonelId = input.PersonelId.Trim(),
            TcKimlikNo = tcKimlikNo,
            Orcid = result.Input.Orcid,
            GoogleScholarId = result.Input.GoogleScholarId,
            WebOfScienceId = result.Input.WebOfScienceResearcherId,
            ScopusId = input.ScopusId?.Trim()
        }, result.Warnings, rejectionReason);
    }
}

public sealed record BulkNormalizationResult(
    BulkResearcherInput Input, List<string> Warnings, string? RejectionReason);
