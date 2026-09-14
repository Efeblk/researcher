using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public static class PublicationEligibilityPolicyCatalog
{
    public static PublicationEligibilityPolicyDto Create() => new()
    {
        IncludedCategories = Enum.GetNames<AcademicWorkCategory>().ToList(),
        Reason = "Every collected category is included in descriptive coverage. This policy does not make a category eligible for ranking, personnel evaluation, or field normalization."
    };
}
