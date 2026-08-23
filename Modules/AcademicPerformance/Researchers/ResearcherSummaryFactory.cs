using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherSummaryFactory
{
    public ResearcherSummary? Create(Researcher? researcher)
    {
        ResearcherSummary? summary = null;

        if (researcher is null)
        {
            return null;
        }

        summary = new ResearcherSummary();
        summary.Id = researcher.Id;
        summary.UniversityPersonnelId = researcher.UniversityPersonnelId;
        summary.FirstName = researcher.FirstName;
        summary.LastName = researcher.LastName;
        summary.AcademicTitle = researcher.AcademicTitle;
        summary.Department = researcher.Department;
        summary.Orcid = researcher.Orcid;
        summary.LastUpdatedAt = researcher.LastUpdatedAt;
        summary.OrcidProfile = CreateOrcidSummary(researcher);
        summary.Metrics = CreateMetricsSummary(researcher.Metrics);

        return summary;
    }

    private static OrcidSummary? CreateOrcidSummary(Researcher researcher)
    {
        OrcidSummary? summary = null;

        if (researcher.OrcidProfile is null)
        {
            return null;
        }

        summary = new OrcidSummary();
        summary.DisplayName = researcher.OrcidProfile.DisplayName;
        summary.WorkCount = researcher.OrcidProfile.Works?.Count
            ?? researcher.OrcidProfile.WorksCount;
        summary.WorkCategories = CreateOrcidCategoryCounts(
            researcher.OrcidProfile.Works);
        summary.CurrentOrganization = researcher.OrcidProfile.CurrentOrganization;
        summary.EmploymentsCount = researcher.OrcidProfile.EmploymentsCount;
        summary.EducationsCount = researcher.OrcidProfile.EducationsCount;
        summary.LastUpdatedAt = researcher.OrcidProfile.LastUpdatedAt;

        return summary;
    }

    private static ResearcherMetricsSummary? CreateMetricsSummary(
        ResearcherMetrics? metrics)
    {
        ResearcherMetricsSummary? summary = null;

        if (metrics is null)
        {
            return null;
        }

        summary = new ResearcherMetricsSummary();
        summary.WorksCount = metrics.WorksCount;
        summary.CitedByCount = metrics.CitedByCount;
        summary.HIndex = metrics.HIndex;
        summary.I10Index = metrics.I10Index;
        summary.Source = metrics.Source;
        summary.UpdatedAt = metrics.UpdatedAt;

        return summary;
    }

    private static Dictionary<string, int> CreateOrcidCategoryCounts(
        List<OrcidWork>? works)
    {
        Dictionary<string, int>? categoryCounts = null;
        int index = 0;
        string? categoryName = null;
        int currentCount = 0;

        categoryCounts = [];

        if (works is null)
        {
            return categoryCounts;
        }

        for (index = 0; index < works.Count; index++)
        {
            categoryName = works[index].Category.ToString();
            categoryCounts.TryGetValue(categoryName, out currentCount);
            categoryCounts[categoryName] = currentCount + 1;
        }

        return categoryCounts;
    }

}
