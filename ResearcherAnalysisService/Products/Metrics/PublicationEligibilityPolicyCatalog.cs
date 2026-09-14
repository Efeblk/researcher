using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.SourceData.Works;

namespace ResearcherAnalysisService.Products.Metrics;

public static class PublicationEligibilityPolicyCatalog
{
    public static PublicationEligibilityPolicyDto Create() => new()
    {
        IncludedCategories = Enum.GetNames<AcademicWorkCategory>().ToList(),
        Reason = "Every collected category is included in descriptive coverage. This policy does not make a category eligible for ranking, personnel evaluation, or field normalization."
    };
}
